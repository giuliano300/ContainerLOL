using Microsoft.EntityFrameworkCore;
using SharedLib.Db;
using Valorizza.Services;
using SharedLib.WsdlModels;
using SharedLib.Utils;
using SharedLib.Models;
using System.Diagnostics;
using SharedLib.Messaging;

public class ValorizzaWatcher : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ValorizzaProcessor> _logger;
    private readonly IValorizzaQueueTracker _tracker;
    private readonly IRabbitPublisher _publisher;

    private const string QUEUE_NAME = "valorizza_lol_queue";

    public ValorizzaWatcher(IConfiguration configuration, IServiceProvider serviceProvider, ILogger<ValorizzaProcessor> logger, IValorizzaQueueTracker tracker,
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
            var delay = _configuration.GetValue<int>("Timers:ValorizzaWatcherSeconds");
            try
            {
                // Create a fresh scope for each polling cycle to keep DbContext lifetime short.
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Select only the columns needed to queue valorizza work; tracking full recipients is unnecessary here.
                var newRecipients = await db.Recipients
                    .AsNoTracking()
                    .Where(r =>
                        r.CurrentState == (int)CurrentState.accettatoOnline &&
                        r.Valid &&
                        r.Operations.Complete &&
                        r.ProductType == (int)ProductTypes.LOL &&
                        r.Format == (int)FormatType.A4 &&
                        r.InProcessStep2 != true)
                    .OrderBy(r => r.Id)
                    .Select(r => new { r.Id, r.RequestId })
                    .Take(20)
                    .ToListAsync(stoppingToken);

                var recipientsToPublish = new List<ValorizzaItem>();

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
                    RecipientWorkflowHelper.MarkQueued(db, recipient, LolWorkflowStep.Valorizza);
                    db.Entry(recipient).Property(x => x.InProcessStep2).IsModified = true;
                    db.Entry(recipient).Property(x => x.worked).IsModified = true;

                    recipientsToPublish.Add(new ValorizzaItem { NameId = r.Id , RequesId = r.RequestId! });
                }

                // Persist queue flags and audit rows before emitting RabbitMQ messages.
                await db.SaveChangesAsync(stoppingToken);

                // Publish only after the database state says these recipients are queued.
                foreach (var item in recipientsToPublish)
                    await _publisher.PublishAsync(QUEUE_NAME, item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Errore nel processor ValorizzaWatcher.");
            }

            // Wait until the next polling cycle unless the service is stopping.
            await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
        }
    }
}
