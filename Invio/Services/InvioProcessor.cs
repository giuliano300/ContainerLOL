using SharedLib.Models;
using ServiceReference;
using SharedLib.Db;
using SharedLib.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedLib.Config;
using SharedLib.WsdlModels;

namespace Invio.Services;

public class InvioProcessor : BackgroundService
{
    private readonly IInvioQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LolServiceOptions _options;
    private readonly ILogger<InvioProcessor> _logger;
    private readonly IInvioQueueTracker _tracker;
    private readonly TimeSpan _delay = TimeSpan.FromSeconds(30); // ogni 30 secondi

    private readonly SemaphoreSlim _executionLock = new(1, 1); // 🔒 Blocca esecuzioni parallele

    public InvioProcessor(
        IInvioQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<LolServiceOptions> options,
        ILogger<InvioProcessor> logger,
        IInvioQueueTracker tracker)
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
                var items = new List<InvioItem>();

                while (_queue.TryDequeue(out var item))
                {
                    items.Add(item);
                }

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                foreach (var item in items)
                {

                    try
                    {
                        var n = await db.Recipients
                            .Include(x => x.Operations).ThenInclude(o => o.Users)
                            .Include(x => x.Operations).ThenInclude(o => o.Senders)
                            .Include(x => x.Bulletins)
                            .FirstOrDefaultAsync(x => x.Id == item.NameId, stoppingToken);

                        if (n == null)
                        {
                            _logger.LogWarning("Recipient non trovato.");
                            continue;
                        }

                        var user = n.Operations.Users;
                        if (user == null)
                        {
                            _logger.LogWarning("Utente non trovato per il destinatario n. {Id}", n.Id);
                            continue;
                        }

                        var service = LOLServiceHelper.GetNewServiceLOL(user, _options);
                        if (service == null)
                        {
                            _logger.LogWarning("Service non creato per l'utente n. {UserId}", user.Id);
                            continue;
                        }

                        var requestId = LOLServiceHelper.GetRequestId(user.Guid, db, _options, service);
                        if (requestId == null)
                        {
                            _logger.LogWarning("RequestId non ottenuto.");
                            continue;
                        }

                        var sender = n.Operations.Senders.FirstOrDefault(a => a.Ar != true);
                        if (sender == null)
                        {
                            _logger.LogWarning("Mittente non trovato per il destinatario n. {Id}", n.Id);
                            continue;
                        }

                        var ls = new LOLSubmit
                        {
                            Mittente = LOLServiceHelper.GetMittente(sender),
                            Opzioni = LOLServiceHelper.GetOpzioniLOL(n.PrintType, n.FrontBack),
                            NumeroDestinatari = 1,
                            Documento = LOLServiceHelper.GetDoc(n.AttachedFile!),
                            Nazionale = (n.State.ToUpper() == "ITALIA" || n.State.ToUpper() == "IT") ? "true" : "false",
                            Destinatari = new[] { LOLServiceHelper.GetDestinatarioLOL(n) }
                        };

                        var bulletin = n.Bulletins.FirstOrDefault();
                        if (bulletin != null)
                        {
                            var pagina = new PaginaBollettino
                            {
                                Bollettino = LOLServiceHelper.GetBollettino896(bulletin)
                            };
                            ls.PagineBollettini = new PaginaBollettinoBase[] { pagina };
                        }

                        string message;
                        n.worked = true;
                        n.InProcess = false;

                        try
                        {
                            var esito = await service.InvioAsync(requestId, user.BusinessName, "", ls);

                            message = esito.CEResult.Description;

                            n.StatoMarker = esito.CEResult.Type;
                            n.RequestId = requestId;
                            n.CurrentState = esito.CEResult.Type == "I"
                                ? (int)CurrentState.accettatoOnline
                                : (int)CurrentState.ErroreSubmit;

                            n.Valid = esito.CEResult.Type == "I";
                            n.Message = message;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Errore durante l'invio WSDL");

                            message = ex.Message;
                            n.CurrentState = (int)CurrentState.ErroreGenerico;
                            n.Valid = false;
                        }

                        db.RecipientWorks.Add(new RecipientWorks
                        {
                            Message = message,
                            RecipientId = n.Id,
                            WorkDate = DateTime.UtcNow,
                            WorkStatus = (int)WorkStatus.InviatoPoste
                        });

                        await db.SaveChangesAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Errore generale nella lavorazione dell'invio.");
                    }
                }
            }
            finally
            {
                _executionLock.Release();
            }

            await Task.Delay(_delay, stoppingToken);
        }
    }
}
