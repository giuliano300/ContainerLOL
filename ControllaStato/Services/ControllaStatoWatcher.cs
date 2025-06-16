using Microsoft.EntityFrameworkCore;
using SharedLib.Db;
using SharedLib.Models;
using ControllaStato.Services;
using SharedLib.Utils;
using SharedLib.WsdlModels;

public class ControllaStatoWatcher : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _delay = TimeSpan.FromSeconds(20); // ogni 20 secondi
    private readonly ILogger<ControllaStatoProcessor> _logger;
    private readonly IControllaStatoQueueTracker _tracker;

    public ControllaStatoWatcher(IServiceProvider serviceProvider, ILogger<ControllaStatoProcessor> logger, IControllaStatoQueueTracker tracker)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _tracker = tracker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var queue = scope.ServiceProvider.GetRequiredService<IControllaStatoQueue>();

                var newRecipients = await db.Recipients
                    .Include(r => r.Operations)
                    .Where(r =>
                        r.CurrentState == (int)CurrentState.presaInCarico &&
                        r.Valid &&
                        r.Operations.Complete &&
                        r.ProductType == (int)ProductTypes.LOL &&
                        r.Format == (int)FormatType.A4 &&
                        r.Code != null && r.Code != "" &&
                        r.finalState != true)
                    .Take(100)
                    .ToListAsync(stoppingToken);

                foreach (var r in newRecipients)
                {
                    if (!_tracker.TryTrack(r.Id))
                    {
                        _logger.LogInformation($"Recipient {r.Id} già in coda per invio. Skippato.");
                        continue;
                    }

                    var item = new ControllaStatoItem { 
                        NameId = r.Id,
                        RequesId = r.RequestId!,
                        StatoMarker = r.StatoMarker!
                    };
                    queue.Enqueue(item);
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel processor ControllaStatoWatcher.");
            }

            await Task.Delay(_delay, stoppingToken);
        }
    }
}
