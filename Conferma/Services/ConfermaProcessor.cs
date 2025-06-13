using Conferma.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceReference;
using SharedLib.Config;
using SharedLib.Db;
using SharedLib.Models;
using SharedLib.Utils;
using SharedLib.WsdlModels;

public class ConfermaProcessor : BackgroundService
{
    private readonly IConfermaQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LolServiceOptions _options;
    private readonly ILogger<ConfermaProcessor> _logger;
    private readonly IConfermaQueueTracker _tracker;
    private readonly TimeSpan _delay = TimeSpan.FromSeconds(30); // ogni 30 secondi

    private readonly SemaphoreSlim _executionLock = new(1, 1); // 🔒 Blocca esecuzioni parallele

    public ConfermaProcessor(IConfermaQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<LolServiceOptions> options,
        ILogger<ConfermaProcessor> logger,
        IConfermaQueueTracker tracker)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _tracker = tracker;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var acquired = await _executionLock.WaitAsync(0, stoppingToken);
            if (!acquired)
            {
                _logger.LogInformation("⏳ Ciclo già in esecuzione, attende la fine del ciclo...");
                await Task.Delay(_delay, stoppingToken);
                continue;
            }
            try
            {

                var items = new List<ConfermaItem>();

                while (_queue.TryDequeue(out var item))
                {
                    items.Add(item);
                }

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                foreach (var item in items)
                {

                    var n = await db.Recipients
                        .Include(x => x.Operations).ThenInclude(o => o.Users)
                        .Include(x => x.Operations).ThenInclude(o => o.Senders)
                        .FirstOrDefaultAsync(x => x.Id == item.NameId, stoppingToken);

                    if (n == null)
                    {
                        _logger.LogError("Recipient non trovato.");
                        return;
                    }

                    try
                    {
                        var user = n.Operations.Users;
                        var service = LOLServiceHelper.GetNewServiceLOL(user, _options);

                        var richieste = new[] { new Richiesta { IDRichiesta = n.RequestId } };
                        var preConferma = await service.PreConfermaAsync(richieste, true);

                        var result = preConferma.PreConfermaResult;

                        if (result.CEResult.Type == "I" && result.DestinatariLettera?.Length > 0)
                        {
                            n.CurrentState = (int)CurrentState.presaInCarico;
                            n.Code = preConferma.PreConfermaResult.DestinatariLettera[0].IdRicevuta;
                            n.Message = "Presa in Carico Poste";

                            var dcs = await service.RecuperaDocumentoFinaleAsync(n.Code);
                            if (dcs.RecuperaDocumentoFinaleResult.CEResult.Type == "I")
                                n.PathRecoveryFile = Convert.ToBase64String(dcs.RecuperaDocumentoFinaleResult.Documento.Contenuto);

                            _logger.LogInformation($"Conferma recipient n.{n.Id}");
                        }
                        else
                        {
                            n.Message = preConferma.PreConfermaResult.CEResult.Description;
                            n.Valid = false;
                            n.CurrentState = (int)CurrentState.ErroreConfirm;

                            _logger.LogWarning($"Errore conferma recipient n.{n.Id}");
                        }

                        n.InProcess = false;
                        n.worked = true;

                        db.RecipientWorks.Add(new RecipientWorks
                        {
                            Message = n.Message,
                            RecipientId = n.Id,
                            WorkDate = DateTime.UtcNow,
                            WorkStatus = (int)WorkStatus.InviatoConferma
                        });

                        await db.SaveChangesAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Errore durante la validazione WSDL");

                        n.CurrentState = (int)CurrentState.ErroreGenerico;
                        n.Message = ex.Message;
                        n.Valid = false;
                        n.InProcess = false;
                        n.worked = true;

                        db.RecipientWorks.Add(new RecipientWorks
                        {
                            Message = ex.Message,
                            RecipientId = n.Id,
                            WorkDate = DateTime.UtcNow,
                            WorkStatus = (int)WorkStatus.InviatoConferma
                        });

                        await db.SaveChangesAsync(stoppingToken);
                    }

                    _tracker.Untrack(n.Id);
                }
            }
            finally
            {
                _executionLock.Release(); // ✅ Semaforo sempre rilasciato
            }

            await Task.Delay(_delay, stoppingToken);
        }
    }
}
