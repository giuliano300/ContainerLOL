using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecuperaDocumentoFinale.Services;
using RabbitMQ.Client;
using SharedLib.Config;
using SharedLib.Db;
using SharedLib.Messaging;
using SharedLib.Models;
using SharedLib.Utils;
using SharedLib.WsdlModels;
using System.Text.Json;
using System.Threading;

public class RecuperaDocumentoFinaleProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LolServiceOptions _options;
    private readonly ILogger<RecuperaDocumentoFinaleProcessor> _logger;
    private readonly IRecuperaDocumentoFinaleQueueTracker _tracker;
    private readonly IConnection _connection;
    private IModel _channel;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private const string QueueName = "recupera_documento_finale_lol_queue";

    /// <summary>
    /// Builds the final-document background processor with its database, RabbitMQ and tracking dependencies.
    /// </summary>
    public RecuperaDocumentoFinaleProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<LolServiceOptions> options,
        ILogger<RecuperaDocumentoFinaleProcessor> logger,
        IRecuperaDocumentoFinaleQueueTracker tracker,
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
    /// Starts the RabbitMQ consumer that dispatches final-document messages one at a time.
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // RabbitConsumerHelper centralizes queue declaration, ack/nack, deserialization errors and local serialization.
        return RabbitConsumerHelper.StartSingleMessageConsumerAsync<ConfermaItem>(
            _channel,
            _semaphore,
            QueueName,
            _logger,
            json => JsonSerializer.Deserialize<ConfermaItem>(json),
            ProcessItemAsync,
            stoppingToken);
    }

    /// <summary>
    /// Loads the recipient and executes one final-document recovery attempt.
    /// </summary>
    private async Task ProcessItemAsync(ConfermaItem item, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            // Load only the user credentials needed for the final-document Poste call.
            var n = await db.Recipients
                .Include(x => x.Operations).ThenInclude(o => o.Users)
                .FirstOrDefaultAsync(x => x.Id == item.NameId, stoppingToken);

            if (n == null)
            {
                _logger.LogError("Recipient non trovato.");
                return;
            }

            if (n.CurrentState != (int)CurrentState.presaInCarico)
            {
                // The recipient is no longer eligible for document recovery; release the step flag.
                _logger.LogWarning(
                    "Recipient {Id} già processato. Stato attuale: {State}",
                    n.Id,
                    n.CurrentState);

                RecipientWorkflowHelper.ReleaseStep(n, LolWorkflowStep.RecuperaDocumentoFinale);
                await db.SaveChangesAsync(stoppingToken);

                return;
            }

            // Poste credentials are stored on the operation user.
            var user = n.Operations.Users;
            if (user == null)
            {
                _logger.LogWarning("Utente non trovato per il destinatario n. {Id}", n.Id);
                await MarkAsFailedAsync(db, n, "Utente non trovato per il destinatario n." + n.Id, stoppingToken);
                return;
            }

            // Document recovery is intentionally outside PosteCallClaims because it can be retried.
            if (LolWorkflowContracts.GetMode(LolWorkflowStep.RecuperaDocumentoFinale) != LolWorkflowMode.Retryable)
            {
                throw new InvalidOperationException("Il contratto RecuperaDocumentoFinale deve restare retryable.");
            }

            var service = LOLServiceHelper.GetNewServiceLOL(user, _options);
            if (service == null)
            {
                _logger.LogWarning("Service non creato per l'utente n. {UserId}", user.Id);
                await MarkAsFailedAsync(db, n, "Service non creato per l'utente n. " + user.Id, stoppingToken);
                return;
            }

            // This Poste call is allowed to run more than once until the final document is available.
            var dcs = await service.RecuperaDocumentoFinaleAsync(n.Code);
            var result = dcs.RecuperaDocumentoFinaleResult;

            if (result.CEResult.Type == "I")
            {
                // Store the recovered document content only when Poste returns a successful result.
                n.PathRecoveryFile = Convert.ToBase64String(result.Documento.Contenuto);
            }

            n.Message = result.CEResult.Description;
            RecipientWorkflowHelper.ReleaseStep(n, LolWorkflowStep.RecuperaDocumentoFinale);
            RecipientWorkflowHelper.AddProcessedWork(
                db,
                n.Id,
                LolWorkflowStep.RecuperaDocumentoFinale,
                result.CEResult.Description);

            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore durante il recupero documento finale");

            var n = await db.Recipients.FirstOrDefaultAsync(x => x.Id == item.NameId, stoppingToken);
            if (n != null)
            {
                // Failure paths release the step so the watcher can decide whether to retry later.
                await MarkAsFailedAsync(db, n, ex.Message, stoppingToken);
            }
        }
        finally
        {
            _tracker.Untrack(item.NameId);
        }
    }

    /// <summary>
    /// Releases a final-document recipient after a failed attempt and records the failure in the work audit.
    /// </summary>
    private static async Task MarkAsFailedAsync(AppDbContext db, Recipients n, string msg, CancellationToken token)
    {
        // The final-document step is retryable, so this method releases flags without adding a Poste claim.
        await RecipientWorkflowHelper.MarkAsFailedAsync(
            db,
            n,
            LolWorkflowStep.RecuperaDocumentoFinale,
            msg,
            token,
            setGenericErrorState: false,
            invalidateRecipient: false);
    }
}
