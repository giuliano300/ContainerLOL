using Microsoft.EntityFrameworkCore;
using SharedLib.Db;
using SharedLib.Models;
using RecuperaDocumentoFinale.Services;
using SharedLib.WsdlModels;
using SharedLib.Utils;
using SharedLib.Messaging;

public class RecuperaDocumentoFinaleWatcher : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _delay = TimeSpan.FromHours(1); // ogni 1 ora
    private readonly ILogger<RecuperaDocumentoFinaleProcessor> _logger;
    private readonly IRecuperaDocumentoFinaleQueueTracker _tracker;
    private readonly IRabbitPublisher _publisher;

    private const string QUEUE_NAME = "recupera_documento_finale_lol_queue";

    public RecuperaDocumentoFinaleWatcher(IServiceProvider serviceProvider, ILogger<RecuperaDocumentoFinaleProcessor> logger, IRecuperaDocumentoFinaleQueueTracker tracker,
                        IRabbitPublisher publisher)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _tracker = tracker;
        _publisher = publisher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var newRecipients = await db.Recipients
                    .Include(r => r.Operations)
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
                    .Take(500)
                    .ToListAsync(stoppingToken);

                var recipientsToPublish = new List<ConfermaItem>();

                foreach (var r in newRecipients)
                {
                    if (!_tracker.TryTrack(r.Id))
                    {
                        _logger.LogInformation($"Recipient {r.Id} già in coda per invio. Skippato.");
                        continue;
                    }

                    r.InProcessStep4 = true;
                    r.worked = false;

                    db.RecipientWorks.Add(new RecipientWorks
                    {
                        Message = "Inserito in coda recupera documento finale",
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
                _logger.LogError(ex, "Errore nel processor RecuperaDocumentoFinaleWatcher.");
            }

            await Task.Delay(_delay, stoppingToken);
        }
    }
}
