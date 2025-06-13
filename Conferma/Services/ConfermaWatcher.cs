using Microsoft.EntityFrameworkCore;
using SharedLib.Db;
using SharedLib.Models;
using SharedLib.Config;
using Microsoft.Extensions.Options;
using Conferma.Services;
using ServiceReference;
using SharedLib.WsdlModels;
using SharedLib.Utils;
using Microsoft.SqlServer.Server;
using System.Diagnostics;

public class ConfermaWatcher : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _delay = TimeSpan.FromSeconds(20); // ogni 20 secondi
    private readonly ILogger<ConfermaProcessor> _logger;
    private readonly IConfermaQueueTracker _tracker;

    public ConfermaWatcher(IServiceProvider serviceProvider, ILogger<ConfermaProcessor> logger, IConfermaQueueTracker tracker)
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
                var queue = scope.ServiceProvider.GetRequiredService<IConfermaQueue>();

                var newRecipients = await db.Recipients
                    .Include(r => r.Operations)
                    .Where(r =>
                        r.CurrentState == (int)CurrentState.documentoValidato &&
                        r.Valid &&
                        r.Operations.Complete &&
                        r.ProductType == (int)ProductTypes.LOL &&
                        r.Format == (int)FormatType.A4 &&
                        r.InProcess != true)
                    .Take(10)
                    .ToListAsync(stoppingToken);

                foreach (var r in newRecipients)
                {
                    if (!_tracker.TryTrack(r.Id))
                    {
                        _logger.LogInformation($"Recipient {r.Id} già in coda per invio. Skippato.");
                        continue;
                    }

                    var item = new ConfermaItem { NameId = r.Id, RequesId = r.RequestId! };
                    queue.Enqueue(item);

                    r.InProcess = true;
                    r.worked = false;

                    db.RecipientWorks.Add(new RecipientWorks
                    {
                        Message = "Inserito in coda",
                        RecipientId = r.Id,
                        WorkDate = DateTime.UtcNow,
                        WorkStatus = (int)WorkStatus.InCodaConferma
                    });

                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel processor InvioWatcher.");
            }

            await Task.Delay(_delay, stoppingToken);
        }
    }
}
