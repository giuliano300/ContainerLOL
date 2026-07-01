using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RabbitMQ.Client;
using ServiceReference;
using SharedLib.Config;
using SharedLib.Db;
using SharedLib.Messaging;
using SharedLib.Models;
using SharedLib.Utils;
using SharedLib.WsdlModels;

namespace Invio.Services;

public class InvioProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LolServiceOptions _options;
    private readonly ILogger<InvioProcessor> _logger;
    private readonly IInvioQueueTracker _tracker;
    private readonly IConnection _connection;
    private IModel _channel;
    private const string QueueName = "invio_lol_queue";

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Builds the invio background processor with its database, RabbitMQ and tracking dependencies.
    /// </summary>
    public InvioProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<LolServiceOptions> options,
        ILogger<InvioProcessor> logger,
        IInvioQueueTracker tracker,
        IConnection connection)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _tracker = tracker;
        _connection = connection;
        _channel = _connection.CreateModel();
    }

    /// <summary>
    /// Starts the RabbitMQ consumer that dispatches invio messages one at a time.
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // RabbitConsumerHelper centralizes queue declaration, ack/nack, deserialization errors and local serialization.
        return RabbitConsumerHelper.StartSingleMessageConsumerAsync(
            _channel,
            _semaphore,
            QueueName,
            _logger,
            JsonConvert.DeserializeObject<InvioItem>,
            ProcessItemAsync,
            stoppingToken);
    }

    /// <summary>
    /// Loads the recipient, claims the Poste invio call and executes the invio workflow.
    /// </summary>
    private async Task ProcessItemAsync(InvioItem item, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            // Load all data needed to build the Poste invio request in a single scoped context.
            var n = await db.Recipients
                .AsSplitQuery()
                .Include(x => x.Operations).ThenInclude(o => o.Users)
                .Include(x => x.Operations).ThenInclude(o => o.Senders)
                .Include(x => x.Bulletins)
                .FirstOrDefaultAsync(x => x.Id == item.NameId, stoppingToken);

            if (n == null)
            {
                _logger.LogError("Recipient non trovato.");
                return;
            }

            if (n.CurrentState != (int)CurrentState.inAttesa)
            {
                // The recipient is no longer eligible for invio; release the local step flag.
                _logger.LogWarning(
                    "Recipient {Id} già processato. Stato attuale: {State}",
                    n.Id,
                    n.CurrentState);

                RecipientWorkflowHelper.ReleaseStep(n, LolWorkflowStep.Invio);
                await db.SaveChangesAsync(stoppingToken);

                return;
            }

            // Poste credentials are stored on the operation user.
            var user = n.Operations.Users;
            if (user == null)
            {
                _logger.LogWarning("Utente non trovato per il destinatario n. {Id}", n.Id);
                await MarkAsFailed(db, n, "Utente non trovato per il destinatario n." + n.Id, stoppingToken);
                return;
            }

            // Build the SOAP client before claiming so configuration errors can still fail normally.
            var service = LOLServiceHelper.GetNewServiceLOL(user, _options);
            if (service == null)
            {
                _logger.LogWarning("Service non creato per l'utente n. {UserId}", user.Id);
                await MarkAsFailed(db, n, "Service non creato per l'utente n. " + n.Id, stoppingToken);
                return;
            }

            // The claim is committed before contacting Poste; duplicates are acked without another call.
            if (LolWorkflowContracts.GetMode(LolWorkflowStep.Invio) != LolWorkflowMode.SingleCall)
            {
                throw new InvalidOperationException("Il contratto Invio deve restare single-call.");
            }

            var invioClaimed = await PosteCallClaimHelper.TryClaimAsync(
                db,
                n.Id,
                PosteCallStep.Invio,
                "Chiamata invio Poste avviata",
                stoppingToken);
            if (!invioClaimed)
            {
                _logger.LogWarning(
                    "Chiamata invio Poste già avviata per recipient {Id}. Messaggio RabbitMQ scartato.",
                    n.Id);

                return;
            }

            // RecuperaIdRichiestaAsync is itself a Poste call, so it is protected by the claim above.
            var requestId = LOLServiceHelper.GetRequestId(user.Guid, db, _options, service);
            if (requestId == null)
            {
                _logger.LogWarning("RequestId non ottenuto.");
                await MarkAsFailed(db, n, "RequestId non ottenuto", stoppingToken);
                return;
            }

            // The sender is required to build the LOLSubmit payload.
            var sender = n.Operations.Senders.FirstOrDefault(a => a.Ar != true);
            if (sender == null)
            {
                _logger.LogWarning("Mittente non trovato per il destinatario n. {Id}", n.Id);
                await MarkAsFailed(db, n, "Mittente non trovato per il destinatario n." + n.Id, stoppingToken);
                return;
            }

            // Compose the request payload from recipient, sender, document and print options.
            var ls = new LOLSubmit
            {
                Mittente = LOLServiceHelper.GetMittente(sender),
                Opzioni = LOLServiceHelper.GetOpzioniLOL(n.PrintType, n.FrontBack),
                NumeroDestinatari = 1,
                Documento = LOLServiceHelper.GetDoc(n.AttachedFile!),
                Nazionale = (n.State.ToUpper() == "ITALIA" || n.State.ToUpper() == "IT") ? "true" : "false",
                Destinatari = new[] { LOLServiceHelper.GetDestinatarioLOL(n) }
            };

            var bulletin = n.Bulletins.FirstOrDefault();
            if (bulletin != null)
            {
                // Attach the first bulletin page when the recipient has bulletin data.
                var pagina = new PaginaBollettino
                {
                    Bollettino = LOLServiceHelper.GetBollettino896(bulletin)
                };
                ls.PagineBollettini = new PaginaBollettinoBase[] { pagina };
            }

            string message;

            try
            {
                // This is the protected Poste invio call.
                var esito = await service.InvioAsync(requestId, user.BusinessName, "", ls);

                message = esito.CEResult.Description;

                // Persist the business state returned by Poste.
                n.StatoMarker = esito.CEResult.Type;
                n.RequestId = requestId;
                n.CurrentState = esito.CEResult.Type == "I"
                    ? (int)CurrentState.accettatoOnline
                    : (int)CurrentState.ErroreSubmit;

                n.Valid = esito.CEResult.Type == "I";
                n.Message = message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore durante l'invio WSDL");

                message = ex.Message;
                n.CurrentState = (int)CurrentState.ErroreGenerico;
                n.Valid = false;
            }

            // Release the step and record the audit row after the processor has a final result.
            RecipientWorkflowHelper.ReleaseStep(n, LolWorkflowStep.Invio);
            RecipientWorkflowHelper.AddProcessedWork(db, n.Id, LolWorkflowStep.Invio, message);

            await db.SaveChangesAsync(stoppingToken);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore generale nella lavorazione dell'invio.");
        }
        finally
        {
            _tracker.Untrack(item.NameId);
            _logger.LogInformation("🔓 Untrack completato per recipient ID {Id}", item.NameId);
        }
    }

    /// <summary>
    /// Marks an invio recipient as failed and records the failure in the work audit.
    /// </summary>
    private async Task MarkAsFailed(AppDbContext db, Recipients n, string msg, CancellationToken token)
    {
        // Failure paths must release the invio step so external recovery can decide the next action.
        await RecipientWorkflowHelper.MarkAsFailedAsync(db, n, LolWorkflowStep.Invio, msg, token);
    }
}
