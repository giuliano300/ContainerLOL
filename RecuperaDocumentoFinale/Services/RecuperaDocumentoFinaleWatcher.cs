using Microsoft.EntityFrameworkCore;
using SharedLib.Db;
using SharedLib.Models;
using RecuperaDocumentoFinale.Services;
using SharedLib.WsdlModels;
using SharedLib.Utils;
using SharedLib.Messaging;

public class RecuperaDocumentoFinaleWatcher : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecuperaDocumentoFinaleProcessor> _logger;
    private readonly IRecuperaDocumentoFinaleQueueTracker _tracker;
    private readonly IRabbitPublisher _publisher;

    private const string QUEUE_NAME = "recupera_documento_finale_lol_queue";

    public RecuperaDocumentoFinaleWatcher(IConfiguration configuration, IServiceProvider serviceProvider, ILogger<RecuperaDocumentoFinaleProcessor> logger, IRecuperaDocumentoFinaleQueueTracker tracker,
                        IRabbitPublisher publisher)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _tracker = tracker;
        _publisher = publisher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Read the polling interval on every loop so configuration reloads can adjust scheduling.
            var delay = _configuration.GetValue<int>("Timers:RecuperaDocumentoWatcherSeconds");
            try
            {
                // Create a fresh scope for each polling cycle to keep DbContext lifetime short.
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Select only the columns needed to queue document retrieval; tracking full recipients is unnecessary here.
                var newRecipients = await db.Recipients
                    .AsNoTracking()
                    .Where(r =>
                        r.CurrentState == (int)CurrentState.presaInCarico &&
                        r.Valid &&
                        r.Operations.Complete &&
                        r.ProductType == (int)ProductTypes.LOL &&
                        r.Format == (int)FormatType.A4 &&
                        r.Code != null && r.Code != "" &&
                        (
                            r.PathRecoveryFile == null || 
                            r.PathRecoveryFile == ""
                        )
                        && r.InProcessStep4 != true
                     )
                    .Select(r => new { r.Id, r.RequestId })
                    .Take(50)
                    .ToListAsync(stoppingToken);

                var recipientsToPublish = new List<ConfermaItem>();

                foreach (var r in newRecipients)
                {
                    // The in-memory tracker avoids duplicate queueing inside this running service instance.
                    if (!_tracker.TryTrack(r.Id))
                    {
                        _logger.LogInformation($"Recipient {r.Id} già in coda per invio. Skippato.");
                        continue;
                    }

                    // Attach a stub entity so only queue flags are updated, not the whole recipient row.
                    var recipient = new Recipients { Id = r.Id };
                    db.Recipients.Attach(recipient);
                    RecipientWorkflowHelper.MarkQueued(db, recipient, LolWorkflowStep.RecuperaDocumentoFinale);
                    db.Entry(recipient).Property(x => x.InProcessStep4).IsModified = true;
                    db.Entry(recipient).Property(x => x.worked).IsModified = true;

                    recipientsToPublish.Add(new ConfermaItem { NameId = r.Id, RequesId = r.RequestId! });
                }

                // Persist queue flags and audit rows before emitting RabbitMQ messages.
                await db.SaveChangesAsync(stoppingToken);

                // Publish only after the database state says these recipients are queued.
                foreach (var item in recipientsToPublish)
                    await _publisher.PublishAsync(QUEUE_NAME, item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel processor RecuperaDocumentoFinaleWatcher.");
            }

            // Wait until the next polling cycle unless the service is stopping.
            await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
        }
    }
}
