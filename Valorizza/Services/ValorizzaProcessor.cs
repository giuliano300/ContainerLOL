using SharedLib.Models;
using ServiceReference;
using SharedLib.Db;
using SharedLib.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedLib.Config;
using SharedLib.WsdlModels;

namespace Valorizza.Services;

public class ValorizzaProcessor : BackgroundService
{
	private readonly IValorizzaQueue _queue;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly LolServiceOptions _options;
    private readonly ILogger<ValorizzaProcessor> _logger;
    private readonly IValorizzaQueueTracker _tracker;
    private readonly TimeSpan _delay = TimeSpan.FromSeconds(30); // ogni 30 secondi

    private readonly SemaphoreSlim _executionLock = new(1, 1); // 🔒 Blocca esecuzioni parallele

    public ValorizzaProcessor(IValorizzaQueue queue, 
		IServiceScopeFactory scopeFactory, 
		IOptions<LolServiceOptions> options,
		ILogger<ValorizzaProcessor> logger,
        IValorizzaQueueTracker tracker)
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
                var items = new List<ValorizzaItem>();

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

                    try
                    {

                        if (n == null)
                        {
                            _logger.LogError("Recipient non trovato.");
                            return;
                        }

                        var user = n.Operations.Users;
                        if (user == null)
                        {
                            _logger.LogError("Utente non trovato per il destinatario n." + n.Id);
                            return;
                        }
                    
					    var service = LOLServiceHelper.GetNewServiceLOL(user, _options);
					    if (service == null)
					    {
                            _logger.LogError("Service non creato per l'utente n." + user.Id);
                            return;
					    };


                        Richiesta[] Richiesta = new Richiesta[1];
                        var r = new Richiesta() { IDRichiesta = n.RequestId };
                        Richiesta[0] = r;
                        var v = await service.ValorizzaAsync(Richiesta);

                        //VERIFICA SE E' IN UNO DEGLI STATI FINALI
                        var st = v.ValorizzaResult.ServizioEnquiryResponse
                            .Where(a => a.StatoLavorazione.Id == "N"
                            || a.StatoLavorazione.Id == "Y"
                            || a.StatoLavorazione.Id == "J"
                            || a.StatoLavorazione.Id == "G"
                            || a.StatoLavorazione.Id == "R"
                            || a.StatoLavorazione.Id == "A"
                            || a.StatoLavorazione.Id == "U"
                            || a.StatoLavorazione.Id == "V"
                            || a.StatoLavorazione.Id == "W"
                            || a.StatoLavorazione.Id == "S"
                            )
                            .Count();

                        n.worked = true;
                        n.InProcess = false;

                        if (st > 0)
                        {

                            // Azzeriamo i tentativi perché è andato a buon fine
                            n.TentativiValorizzazione = 0;

                            var s = v.ValorizzaResult.ServizioEnquiryResponse.FirstOrDefault();

                            n.Message = s!.StatoLavorazione.Descrizione.Replace("Postel", "Poste");

                            switch (s.StatoLavorazione.Id)
                            {
                                case "R":
                                case "L":
                                    n.CurrentState = (int)CurrentState.documentoValidato;
                                    n.Price = Convert.ToDecimal(s.Totale.ImportoNetto);
                                    n.VatPrice = Convert.ToDecimal(s.Totale.ImportoIva);
                                    n.TotalPrice = Convert.ToDecimal(s.Totale.ImportoTotale);
                                    n.Valid = true;
                                    break;
                                default:
                                    n.CurrentState = (int)CurrentState.ErroreValidazione;
                                    n.Valid = false;
                                    break;
                            }


                            _logger.LogInformation($"Valorizzazione recipient n.{n.Id}");

                        }
                        else
                        {
                            n.TentativiValorizzazione++;

                            if (n.TentativiValorizzazione >= 3)
                            {
                                n.CurrentState = (int)CurrentState.ErroreValidazione;
                                n.Message = "Errore dopo 3 tentativi di valorizzazione/Rinviare";

                                _logger.LogWarning($"Recipient n.{n.Id} segnato con errore dopo 3 tentativi di valorizzazione.");
                            }
                        }

                        //Inviato a poste
                        RecipientWorks recipientWorks = new RecipientWorks()
                        {
                            Message = n.Message,
                            RecipientId = n.Id,
                            WorkDate = DateTime.UtcNow,
                            WorkStatus = (int)WorkStatus.InviatoValorizza
                        };

                        db.RecipientWorks.Add(recipientWorks);
                        
                        await db.SaveChangesAsync(stoppingToken);


                    }
                    catch (Exception ex)
				    {
                        _logger.LogError(ex, "Errore durante la validazione WSDL");

                        n!.CurrentState = (int)CurrentState.ErroreGenerico;
                        n.Message = ex.Message;

                        n.Valid = false;
                        n.InProcess = false;
                        n.worked = true;

                        db.RecipientWorks.Add(new RecipientWorks
                        {
                            Message = ex.Message,
                            RecipientId = n.Id,
                            WorkDate = DateTime.UtcNow,
                            WorkStatus = (int)WorkStatus.InviatoValorizza
                        });

                        await db.SaveChangesAsync(stoppingToken);
                    }


                    _tracker.Untrack(n.Id); // dopo aver salvato o gestito l’errore libera la coda
            
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
