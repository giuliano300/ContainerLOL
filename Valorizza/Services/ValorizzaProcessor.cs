using SharedLib.Models;
using ServiceReference;
using SharedLib.Db;
using SharedLib.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedLib.Config;
using SharedLib.WsdlModels;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Newtonsoft.Json;

namespace Valorizza.Services;

public class ValorizzaProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LolServiceOptions _options;
    private readonly ILogger<ValorizzaProcessor> _logger;
    private readonly IValorizzaQueueTracker _tracker;
    private readonly IConnection _connection;
    private IModel _channel;
    private const string QueueName = "valorizza_lol_queue";

    public ValorizzaProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<LolServiceOptions> options,
        ILogger<ValorizzaProcessor> logger,
        IValorizzaQueueTracker tracker,
        IConnection connection)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
        _tracker = tracker;
        _connection = connection;
        _channel = _connection.CreateModel();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel.QueueDeclare(queue: QueueName, durable: true, exclusive: false, autoDelete: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);

            ValorizzaItem? item;
            try
            {
                item = JsonConvert.DeserializeObject<ValorizzaItem>(json);
                if (item == null)
                {
                    _logger.LogWarning("Messaggio non valido: {Json}", json);
                    _channel.BasicAck(ea.DeliveryTag, false);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore nella deserializzazione del messaggio RabbitMQ.");
                _channel.BasicAck(ea.DeliveryTag, false);
                return;
            }

            await ProcessItemAsync(item, stoppingToken);
            _channel.BasicAck(ea.DeliveryTag, false);
        };

        _channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);

        return Task.CompletedTask;
    }

    private async Task ProcessItemAsync(ValorizzaItem item, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
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

            var user = n.Operations.Users;
            if (user == null)
            {
                _logger.LogError("Utente non trovato per il destinatario n." + n.Id);
                await MarkAsFailed(n, "Utente non trovato per il destinatario n." + n.Id, stoppingToken);
                return;
            }

            var service = LOLServiceHelper.GetNewServiceLOL(user, _options);
            if (service == null)
            {
                _logger.LogError("Service non creato per l'utente n." + user.Id);
                await MarkAsFailed(n, "Service non creato per l'utente n." + user.Id, stoppingToken);
                return;
            }

            var richiesta = new[] { new Richiesta { IDRichiesta = n.RequestId } };
            var v = await service.ValorizzaAsync(richiesta);

            var st = v.ValorizzaResult.ServizioEnquiryResponse
                .Count(a => new[] { "N", "Y", "J", "G", "R", "A", "U", "V", "W", "S", "L" }
                .Contains(a.StatoLavorazione.Id));

            string? message =null;
            try
            {
                if (st > 0)
                {
                    n.TentativiValorizzazione = 0;
                    var s = v.ValorizzaResult.ServizioEnquiryResponse.FirstOrDefault();
                    if (s != null)
                    {
                        message = s.StatoLavorazione.Descrizione.Replace("Postel", "Poste");

                        if (s.StatoLavorazione.Id is "R" or "L")
                        {
                            n.CurrentState = (int)CurrentState.documentoValidato;
                            n.Price = Convert.ToDecimal(s.Totale.ImportoNetto);
                            n.VatPrice = Convert.ToDecimal(s.Totale.ImportoIva);
                            n.TotalPrice = Convert.ToDecimal(s.Totale.ImportoTotale);
                            n.Valid = true;
                        }
                        else
                        {
                            n.CurrentState = (int)CurrentState.ErroreValidazione;
                            n.Valid = false;
                        }
                    }
                }
                else
                {
                    message = "Valorizzazione non ancora disponibile";
                    n.TentativiValorizzazione++;
                    if (n.TentativiValorizzazione >= 3)
                    {
                        n.CurrentState = (int)CurrentState.ErroreValidazione;
                        n.Valid = false;
                        message = "Errore dopo 3 tentativi di valorizzazione/Rinviare";
                    }
                }

            }

            catch (Exception e)
            {
                message = e.Message;
                await MarkAsFailed(n, message, stoppingToken);
                return;
            }

            n.Message = message;
            n.worked = true;
            n.InProcessStep2 = false;

            db.RecipientWorks.Add(new RecipientWorks
            {
                Message = message,
                RecipientId = n.Id,
                WorkDate = DateTime.UtcNow,
                WorkStatus = (int)WorkStatus.InviatoValorizza
            });

            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore durante la validazione WSDL");

            var n = await db.Recipients.FirstOrDefaultAsync(x => x.Id == item.NameId, stoppingToken);
            if (n != null)
            {
                n.CurrentState = (int)CurrentState.ErroreGenerico;
                n.Message = ex.Message;
                n.Valid = false;
                n.InProcessStep2 = false;
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
        }
        finally
        {
            _tracker.Untrack(item.NameId);
        }
    }

    private async Task MarkAsFailed(Recipients n, string msg, CancellationToken token)
    {
        n.InProcessStep2 = false;
        n.worked = true;
        n.CurrentState = (int)CurrentState.ErroreGenerico;
        n.Message = msg;
        n.Valid = false;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.RecipientWorks.Add(new RecipientWorks
        {
            Message = msg,
            RecipientId = n.Id,
            WorkDate = DateTime.UtcNow,
            WorkStatus = (int)WorkStatus.InviatoPoste
        });

        await db.SaveChangesAsync(token);
        _tracker.Untrack(n.Id);
    }


}
