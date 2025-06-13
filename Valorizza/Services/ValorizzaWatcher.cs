using Microsoft.EntityFrameworkCore;
using SharedLib.Db;
using Valorizza.Services;
using SharedLib.WsdlModels;
using SharedLib.Utils;
using SharedLib.Models;
using System.Diagnostics;

public class ValorizzaWatcher : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _delay = TimeSpan.FromSeconds(20);
    private readonly ILogger<ValorizzaProcessor> _logger;
    private readonly IValorizzaQueueTracker _tracker;

    public ValorizzaWatcher(IServiceProvider serviceProvider, ILogger<ValorizzaProcessor> logger, IValorizzaQueueTracker tracker)
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
                var queue = scope.ServiceProvider.GetRequiredService<IValorizzaQueue>();

                var newRecipients = await db.Recipients
                    .Include(r => r.Operations)
                    .Where(r => r.CurrentState == (int)CurrentState.accettatoOnline &&
                        r.Valid &&
                        r.Operations.Complete &&
                        r.ProductType == (int)ProductTypes.LOL &&
                        r.Format == (int)FormatType.A4 &&
                        r.InProcess != true)
                    .Take(10)
                    .ToListAsync();

                foreach (var r in newRecipients)
                {
                    if (!_tracker.TryTrack(r.Id))
                    {
                        _logger.LogInformation($"Recipient {r.Id} già in coda per valorizza. Skippato.");
                        continue;
                    }

                    ValorizzaItem item = new ValorizzaItem()
                    {
                        NameId = r.Id,
                        RequesId = r.RequestId!
                    };

                    queue.Enqueue(item);

                    r.InProcess = true;
                    r.worked = false;

                    RecipientWorks recipientWorks = new RecipientWorks()
                    {
                        Message = "Inserito in coda",
                        RecipientId = r.Id,
                        WorkDate = DateTime.UtcNow,
                        WorkStatus = (int)WorkStatus.InCodaValorizza
                    };
                    db.RecipientWorks.Add(recipientWorks);
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nel processor InvioWatcher.");
            }

            await Task.Delay(_delay, stoppingToken);
        }
    }
}
