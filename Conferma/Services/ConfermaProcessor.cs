using Conferma.Services;
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

public class ConfermaProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LolServiceOptions _options;
    private readonly ILogger<ConfermaProcessor> _logger;
    private readonly IConfermaQueueTracker _tracker;
    private readonly IConnection _connection;
    private IModel _channel;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private const string QueueName = "conferma_lol_queue";

    /// <summary>
    /// Builds the conferma background processor with its database, RabbitMQ and tracking dependencies.
    /// </summary>
    public ConfermaProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<LolServiceOptions> options,
        ILogger<ConfermaProcessor> logger,
        IConfermaQueueTracker tracker,
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
    /// Starts the RabbitMQ consumer that dispatches conferma messages one at a time.
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // RabbitConsumerHelper centralizes queue declaration, ack/nack, deserialization errors and local serialization.
        return RabbitConsumerHelper.StartSingleMessageConsumerAsync(
            _channel,
            _semaphore,
            QueueName,
            _logger,
            JsonConvert.DeserializeObject<ConfermaItem>,
            ProcessItemAsync,
            stoppingToken);
    }

    /// <summary>
    /// Loads the recipient, claims the Poste conferma call and executes the conferma workflow.
    /// </summary>
    private async Task ProcessItemAsync(ConfermaItem item, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            // Load the recipient and operation user required to call Poste.
            var n = await db.Recipients
                .Include(x => x.Operations).ThenInclude(o => o.Users)
                .FirstOrDefaultAsync(x => x.Id == item.NameId, stoppingToken);

            if (n == null)
            {
                _logger.LogError("Recipient non trovato.");
                return;
            }

            if (n.CurrentState != (int)CurrentState.documentoValidato)
            {
                // The recipient is no longer eligible for conferma; release the local step flag.
                _logger.LogWarning(
                    "Recipient {Id} già processato. Stato attuale: {State}",
                    n.Id,
                    n.CurrentState);

                RecipientWorkflowHelper.ReleaseStep(n, LolWorkflowStep.Conferma);
                await db.SaveChangesAsync(stoppingToken);

                return;
            }

            // Build the SOAP client using the operation user credentials.
            var user = n.Operations.Users;
            var service = LOLServiceHelper.GetNewServiceLOL(user, _options);

            // The claim is committed before contacting Poste; duplicates are acked without another call.
            if (LolWorkflowContracts.GetMode(LolWorkflowStep.Conferma) != LolWorkflowMode.SingleCall)
            {
                throw new InvalidOperationException("Il contratto Conferma deve restare single-call.");
            }

            var confermaClaimed = await PosteCallClaimHelper.TryClaimAsync(
                db,
                n.Id,
                PosteCallStep.Conferma,
                "Chiamata conferma Poste avviata",
                stoppingToken);
            if (!confermaClaimed)
            {
                _logger.LogWarning(
                    "Chiamata conferma Poste già avviata per recipient {Id}. Messaggio RabbitMQ scartato.",
                    n.Id);

                return;
            }

            // Build the request after the claim so duplicate messages cannot reach Poste.
            var richieste = new[] { new Richiesta { IDRichiesta = n.RequestId } };
            // This is the protected Poste conferma call.
            var preConferma = await service.PreConfermaAsync(richieste, true);
            var result = preConferma.PreConfermaResult;

            if (result.CEResult.Type == "I" && result.DestinatariLettera?.Length > 0)
            {
                // Poste accepted the confirm call; move the recipient to presaInCarico.
                n.CurrentState = (int)CurrentState.presaInCarico;
                n.Code = result.DestinatariLettera[0].IdRicevuta;
                n.Message = "Presa in Carico Poste";

                // Existing behavior: fetch the final document immediately when conferma succeeds.
                var dcs = await service.RecuperaDocumentoFinaleAsync(n.Code);
                if (dcs.RecuperaDocumentoFinaleResult.CEResult.Type == "I")
                    n.PathRecoveryFile = Convert.ToBase64String(dcs.RecuperaDocumentoFinaleResult.Documento.Contenuto);

                _logger.LogInformation($"Conferma recipient n.{n.Id}");
            }
            else
            {
                // Poste rejected the confirm call; persist the error state for external handling.
                n.Message = result.CEResult.Description;
                n.Valid = false;
                n.CurrentState = (int)CurrentState.ErroreConfirm;

                _logger.LogWarning($"Errore conferma recipient n.{n.Id}");
            }

            // Release the step and record the audit row after the processor has a final result.
            RecipientWorkflowHelper.ReleaseStep(n, LolWorkflowStep.Conferma);
            RecipientWorkflowHelper.AddProcessedWork(db, n.Id, LolWorkflowStep.Conferma, n.Message);

            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore durante la validazione WSDL");

            var n = await db.Recipients.FirstOrDefaultAsync(x => x.Id == item.NameId, stoppingToken);
            if (n != null)
            {
                // Failure paths must release the conferma step so external recovery can decide the next action.
                await RecipientWorkflowHelper.MarkAsFailedAsync(
                    db,
                    n,
                    LolWorkflowStep.Conferma,
                    ex.Message,
                    stoppingToken);
            }
        }
        finally
        {
            _tracker.Untrack(item.NameId);
        }
    }
}
