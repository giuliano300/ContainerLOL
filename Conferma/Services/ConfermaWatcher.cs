using Microsoft.EntityFrameworkCore;
using SharedLib.Db;
using SharedLib.Models;
using Conferma.Services;
using SharedLib.WsdlModels;
using SharedLib.Utils;
using SharedLib.Messaging; // per IRabbitPublisher

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
            var delay = _configuration.GetValue<int>("Timers:ConfermaWatcherSeconds");
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var newRecipients = await db.Recipients
                    .Where(r =>
                        r.CurrentState == (int)CurrentState.documentoValidato &&
                        r.Valid &&
                        r.Operations.Complete &&
                        r.ProductType == (int)ProductTypes.LOL &&
                        r.Format == (int)FormatType.A4 &&
                        r.InProcessStep3 != true)
                    .OrderBy(r => r.Id)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                var recipientsToPublish = new List<ConfermaItem>();

                foreach (var r in newRecipients)
                {
                    if (!_tracker.TryTrack(r.Id))
                    {
                        _logger.LogInformation($"Recipient {r.Id} già in coda per invio. Skippato.");
                        continue;
                    }

                    r.InProcessStep3 = true;
                    r.worked = false;

                    db.RecipientWorks.Add(new RecipientWorks
                    {
                        Message = "Inserito in coda conferma",
                        RecipientId = r.Id,
                        WorkDate = DateTime.UtcNow,
                        WorkStatus = (int)WorkStatus.InCodaConferma
                    });

                    recipientsToPublish.Add(new ConfermaItem { NameId = r.Id, RequesId = r.RequestId! });
                }

                await db.SaveChangesAsync(stoppingToken);

                // 📤 Poi pubblichi su RabbitMQ
                foreach (var item in recipientsToPublish)
                    await _publisher.PublishAsync(QUEUE_NAME, item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Errore nel processor ConfermaWatcher.");
            }

            await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
        }
    }
}
