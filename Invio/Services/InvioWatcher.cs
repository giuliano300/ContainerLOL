using Microsoft.EntityFrameworkCore;
using SharedLib.Db;
using SharedLib.Models;
using Invio.Services;
using SharedLib.WsdlModels;
using SharedLib.Utils;
using SharedLib.Messaging; // per IRabbitPublisher

public class InvioWatcher : BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InvioProcessor> _logger;
    private readonly IInvioQueueTracker _tracker;
    private readonly IRabbitPublisher _publisher;

    private const string QUEUE_NAME = "invio_lol_queue";

    public InvioWatcher(IConfiguration configuration,
                        IServiceProvider serviceProvider,
                        ILogger<InvioProcessor> logger,
                        IInvioQueueTracker tracker,
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
            var delay = _configuration.GetValue<int>("Timers:InvioWatcherSeconds");
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var newRecipients = await db.Recipients
                    .Include(r => r.Operations)
                    .Where(r =>
                        r.CurrentState == (int)CurrentState.inAttesa &&
                        r.Valid &&
                        r.Operations.Complete &&
                        r.ProductType == (int)ProductTypes.LOL &&
                        r.Format == (int)FormatType.A4 &&
                        r.InProcessStep1 != true)
                    .OrderBy(r => r.Id)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                var recipientsToPublish = new List<InvioItem>();

                foreach (var r in newRecipients)
                {
                    if (!_tracker.TryTrack(r.Id))
                    {
                        _logger.LogInformation($"Recipient {r.Id} già in coda per invio. Skippato.");
                        continue;
                    }

                    r.InProcessStep1 = true;
                    r.worked = false;

                    db.RecipientWorks.Add(new RecipientWorks
                    {
                        Message = "Inserito in coda invio",
                        RecipientId = r.Id,
                        WorkDate = DateTime.UtcNow,
                        WorkStatus = (int)WorkStatus.InCodaInvio
                    });

                    recipientsToPublish.Add(new InvioItem { NameId = r.Id });
                }

                await db.SaveChangesAsync(stoppingToken);

                // 📤 Poi pubblichi su RabbitMQ
                foreach (var item in recipientsToPublish)
                    await _publisher.PublishAsync(QUEUE_NAME, item);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Errore nel processor InvioWatcher.");
            }

            await Task.Delay(TimeSpan.FromSeconds(delay), stoppingToken);
        }
    }
}
