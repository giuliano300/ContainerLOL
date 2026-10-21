using Microsoft.EntityFrameworkCore;
using SharedLib.Db;
using SharedLib.Models;
using Conferma.Services;
using SharedLib.WsdlModels;
using SharedLib.Utils;
using SharedLib.Messaging;
using SharedLib.Services; // per IRabbitPublisher

public class ConfermaWatcher : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ConfermaProcessor> _logger;
    private readonly IConfermaQueueTracker _tracker;
    private readonly IRabbitPublisher _publisher;

    private const string QUEUE_NAME = "conferma_lol_queue";

    public ConfermaWatcher(IConfiguration configuration, 
        IServiceProvider serviceProvider, ILogger<ConfermaProcessor> logger, IConfermaQueueTracker tracker,
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
            var delay = _configuration.GetValue<int>("Timers:ConfermaWatcherSeconds");
            var batchSize = 20;
            try
            {
                // Create a fresh scope for each polling cycle to keep DbContext lifetime short.
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var schedule = await AutomationRuntimeSchedule.ReadAsync(db, "LOL:Conferma", stoppingToken);
                if (schedule is not null)
                {
                    delay = (schedule.IntervalMinutes ?? Math.Max(1, delay / 60)) * 60;
                    batchSize = schedule.BatchSize ?? batchSize;
                    var manualRun = schedule.ManualRunRequestedAt.HasValue &&
                        await AutomationRuntimeSchedule.TryConsumeManualRunAsync(db, "LOL:Conferma", stoppingToken);
                    if (!schedule.Enabled || (!manualRun && !schedule.IsInWindow(DateTime.Now)))
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                        continue;
                    }
                }

                // Select only the columns needed to queue conferma work; tracking full recipients is unnecessary here.
                var newRecipients = await db.Recipients
                    .AsNoTracking()
                    .Where(r =>
                        r.CurrentState == (int)CurrentState.documentoValidato &&
                        r.Valid &&
                        r.Operations.Complete &&
                        r.ProductType == (int)ProductTypes.LOL &&
                        r.Format == (int)FormatType.A4 &&
                        r.InProcessStep3 != true)
                    .OrderBy(r => r.Id)
                    .Select(r => new { r.Id, r.RequestId })
                    .Take(batchSize)
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
                    RecipientWorkflowHelper.MarkQueued(db, recipient, LolWorkflowStep.Conferma);
                    db.Entry(recipient).Property(x => x.InProcessStep3).IsModified = true;
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
                _logger.LogError(ex, "❌ Errore nel processor ConfermaWatcher.");
            }

            // Wait until the next polling cycle unless the service is stopping.
            await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
        }
    }
}
