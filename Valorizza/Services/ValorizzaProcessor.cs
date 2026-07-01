using SharedLib.Models;
using ServiceReference;
using SharedLib.Db;
using SharedLib.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedLib.Config;
using SharedLib.WsdlModels;
using RabbitMQ.Client;
using Newtonsoft.Json;
using System.Threading;
using SharedLib.Messaging;

namespace Valorizza.Services;

public class ValorizzaProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LolServiceOptions _options;
    private readonly ILogger<ValorizzaProcessor> _logger;
    private readonly IValorizzaQueueTracker _tracker;
    private readonly IConnection _connection;
    private IModel _channel;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private const string QueueName = "valorizza_lol_queue";

    /// <summary>
    /// Builds the valorizza background processor with its database, RabbitMQ and tracking dependencies.
    /// </summary>
    public ValorizzaProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<LolServiceOptions> options,
        ILogger<ValorizzaProcessor> logger,
        IValorizzaQueueTracker tracker,
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
    /// Starts the RabbitMQ consumer that dispatches valorizza messages one at a time.
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // RabbitConsumerHelper centralizes queue declaration, ack/nack, deserialization errors and local serialization.
        return RabbitConsumerHelper.StartSingleMessageConsumerAsync(
            _channel,
            _semaphore,
            QueueName,
            _logger,
            JsonConvert.DeserializeObject<ValorizzaItem>,
            ProcessItemAsync,
            stoppingToken);
    }

    /// <summary>
    /// Loads the recipient and executes one valorizza polling attempt.
    /// </summary>
    private async Task ProcessItemAsync(ValorizzaItem item, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            // Load the recipient and operation data required for one valorizza polling attempt.
            var n = await db.Recipients
                .Include(x => x.Operations).ThenInclude(o => o.Users)
                .FirstOrDefaultAsync(x => x.Id == item.NameId, stoppingToken);

            if (n == null)
            {
                _logger.LogError("Recipient non trovato.");
                return;
            }

            if (n.CurrentState != (int)CurrentState.accettatoOnline)
            {
                // The recipient is no longer eligible for valorizza; release the valorizza step flag.
                _logger.LogWarning(
                    "Recipient {Id} non pronto per la richiesta. Stato attuale: {State}",
                    n.Id,
                    n.CurrentState);

                RecipientWorkflowHelper.ReleaseStep(n, LolWorkflowStep.Valorizza);
                await db.SaveChangesAsync(stoppingToken);

                return;
            }

            // Poste credentials are stored on the operation user.
            var user = n.Operations.Users;
            if (user == null)
            {
                _logger.LogError("Utente non trovato per il destinatario n." + n.Id);
                await MarkAsFailed(db, n, "Utente non trovato per il destinatario n." + n.Id, stoppingToken);
                return;
            }

            // Valorizza is polling, so it intentionally does not use PosteCallClaims.
            if (LolWorkflowContracts.GetMode(LolWorkflowStep.Valorizza) != LolWorkflowMode.Polling)
            {
                throw new InvalidOperationException("Il contratto Valorizza deve restare polling.");
            }

            var service = LOLServiceHelper.GetNewServiceLOL(user, _options);
            if (service == null)
            {
                _logger.LogError("Service non creato per l'utente n." + user.Id);
                await MarkAsFailed(db, n, "Service non creato per l'utente n." + user.Id, stoppingToken);
                return;
            }

            var richiesta = new[] { new Richiesta { IDRichiesta = n.RequestId } };
            // This Poste call is allowed to run multiple times until a final valuation state is returned.
            var v = await service.ValorizzaAsync(richiesta);

            // Count response rows with states that are meaningful for the valuation workflow.
            var st = v.ValorizzaResult.ServizioEnquiryResponse
                .Count(a => new[] { "N", "Y", "J", "G", "R", "A", "U", "V", "W", "S", "L" }
                .Contains(a.StatoLavorazione.Id));

            string? message =null;
            try
            {
                if (st > 0)
                {
                    // A meaningful response resets the retry counter.
                    n.TentativiValorizzazione = 0;
                    var s = v.ValorizzaResult.ServizioEnquiryResponse.FirstOrDefault();
                    if (s != null)
                    {
                        message = s.StatoLavorazione.Descrizione.Replace("Postel", "Poste");

                        if (s.StatoLavorazione.Id is "R" or "L")
                        {
                            // R/L are considered valid document states for the next conferma step.
                            n.CurrentState = (int)CurrentState.documentoValidato;
                            n.Price = Convert.ToDecimal(s.Totale.ImportoNetto);
                            n.VatPrice = Convert.ToDecimal(s.Totale.ImportoIva);
                            n.TotalPrice = Convert.ToDecimal(s.Totale.ImportoTotale);
                            n.Valid = true;
                        }
                        else
                        {
                            // Any other meaningful state is a validation error for this recipient.
                            n.CurrentState = (int)CurrentState.ErroreValidazione;
                            n.Valid = false;
                        }
                    }
                }
                else
                {
                    // No meaningful response: keep polling until the configured attempt limit is reached.
                    message = "Valorizzazione non ancora disponibile";
                    n.TentativiValorizzazione++;
                    if (n.TentativiValorizzazione >= 3)
                    {
                        n.CurrentState = (int)CurrentState.ErroreValidazione;
                        n.Valid = false;
                        message = "Errore dopo 3 tentativi di valorizzazione/Rinviare";
                    }
                }

            }

            catch (Exception e)
            {
                message = e.Message;
                await MarkAsFailed(db, n, message, stoppingToken);
                return;
            }

            // Release the step and record the audit row after this polling attempt completes.
            n.Message = message;
            RecipientWorkflowHelper.ReleaseStep(n, LolWorkflowStep.Valorizza);
            RecipientWorkflowHelper.AddProcessedWork(db, n.Id, LolWorkflowStep.Valorizza, message);

            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore durante la validazione WSDL");

            var n = await db.Recipients.FirstOrDefaultAsync(x => x.Id == item.NameId, stoppingToken);
            if (n != null)
            {
                // Failure paths must release the valorizza step so external recovery can decide the next action.
                await RecipientWorkflowHelper.MarkAsFailedAsync(
                    db,
                    n,
                    LolWorkflowStep.Valorizza,
                    ex.Message,
                    stoppingToken);
            }
        }
        finally
        {
            _tracker.Untrack(item.NameId);
        }
    }

    /// <summary>
    /// Marks a valorizza recipient as failed and records the failure in the work audit.
    /// </summary>
    private async Task MarkAsFailed(AppDbContext db, Recipients n, string msg, CancellationToken token)
    {
        // MarkAsFailed belongs to the valorizza step, so it must release InProcessStep2.
        await RecipientWorkflowHelper.MarkAsFailedAsync(db, n, LolWorkflowStep.Valorizza, msg, token);
    }

}
