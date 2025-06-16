using SharedLib.Models;
using ServiceReference;
using SharedLib.Db;
using SharedLib.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedLib.Config;
using SharedLib.WsdlModels;

namespace ControllaStato.Services;

public class ControllaStatoProcessor : BackgroundService
{
    private readonly IControllaStatoQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LolServiceOptions _options;
    private readonly ILogger<ControllaStatoProcessor> _logger;
    private readonly IControllaStatoQueueTracker _tracker;
    private readonly TimeSpan _delay = TimeSpan.FromSeconds(30); // ogni 30 secondi

    private readonly SemaphoreSlim _executionLock = new(1, 1); // 🔒 Blocca esecuzioni parallele

    public ControllaStatoProcessor(
        IControllaStatoQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<LolServiceOptions> options,
        ILogger<ControllaStatoProcessor> logger,
        IControllaStatoQueueTracker tracker)
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
                _logger.LogInformation("⏳ Ciclo controllaStato già in esecuzione, attende la fine del ciclo...");
                await Task.Delay(_delay, stoppingToken);
                continue;
            }

            try
            {
                var items = new List<ControllaStatoItem>();

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

                        string message = n.Message;

                        try
                        {

                            RecuperaEsitiRequest recupera = new RecuperaEsitiRequest()
                            {
                                IdRichieste = new string[]
                                {
                                    item.RequesId
                                }
                            };

                            var esito = await service.RecuperaEsitiAsync(recupera);

                            if (esito.RecuperaEsitiResult.RendicontazioneEsiti != null) 
                            {
                                message = esito.RecuperaEsitiResult.RendicontazioneEsiti[0].DescrizioneEsito;
                                n.StatoMarker = esito.RecuperaEsitiResult.RendicontazioneEsiti[0].CodiceEsito;

                                //TROVARE GLI ALTRI CODICI
                                switch (n.StatoMarker.ToUpper())
                                {
                                    case "L":
                                        n.finalState = true; 
                                        break;
                                }

                                n.Message = message;

                                var h = new HistoricRecipientStatus()
                                {
                                    InsertDate = DateTime.Now,
                                    Message = message,
                                    RecipientId = item.NameId
                                };

                                db.HistoricRecipientStatus.Add(h);

                                await db.SaveChangesAsync(stoppingToken);
                            }

                        
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Errore durante il ControllaStato WSDL");

                        }

                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Errore generale nella lavorazione del ControllaStato.");
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
