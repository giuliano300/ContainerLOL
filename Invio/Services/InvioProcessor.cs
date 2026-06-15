using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ServiceReference;
using SharedLib.Config;
using SharedLib.Db;
using SharedLib.Models;
using SharedLib.Utils;
using SharedLib.WsdlModels;
using System.Text;

namespace Invio.Services;

public class InvioProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LolServiceOptions _options;
    private readonly ILogger<InvioProcessor> _logger;
    private readonly IInvioQueueTracker _tracker;
    private readonly IConnection _connection;
    private IModel _channel;
    private const string QueueName = "invio_lol_queue";

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public InvioProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<LolServiceOptions> options,
        ILogger<InvioProcessor> logger,
        IInvioQueueTracker tracker,
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

        _channel.BasicQos(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (model, ea) =>
        {

            var acquired = await _semaphore.WaitAsync(0, stoppingToken);
            if (!acquired)
            {
                _logger.LogWarning("⚠️ Processo già in esecuzione. Skip del messaggio.");
                _channel.BasicNack(ea.DeliveryTag, false, requeue: true); // rimanda il messaggio in coda
                return;
            }
            try
            {
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);

                InvioItem? item;
                try
                {
                    item = JsonConvert.DeserializeObject<InvioItem>(json);
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
            }
            finally
            {
                _semaphore.Release();
            }
        };

        _channel.BasicConsume(queue: QueueName, autoAck: false, consumer: consumer);

        return Task.CompletedTask;
    }

    private async Task ProcessItemAsync(InvioItem item, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            var n = await db.Recipients
                .Include(x => x.Operations).ThenInclude(o => o.Users)
                .Include(x => x.Operations).ThenInclude(o => o.Senders)
                .Include(x => x.Bulletins)
                .FirstOrDefaultAsync(x => x.Id == item.NameId, stoppingToken);

            if (n == null)
            {
                _logger.LogError("Recipient non trovato.");
                return;
            }

            if (n.CurrentState != (int)CurrentState.inAttesa)
            {
                _logger.LogWarning(
                    "Recipient {Id} già processato. Stato attuale: {State}",
                    n.Id,
                    n.CurrentState);

                n.InProcessStep1 = false;
                n.worked = true;
                await db.SaveChangesAsync(stoppingToken);

                return;
            }

            var user = n.Operations.Users;
            if (user == null)
            {
                _logger.LogWarning("Utente non trovato per il destinatario n. {Id}", n.Id);
                await MarkAsFailed(db, n, "Utente non trovato per il destinatario n." + n.Id, stoppingToken);
                return;
            }

            var service = LOLServiceHelper.GetNewServiceLOL(user, _options);
            if (service == null)
            {
                _logger.LogWarning("Service non creato per l'utente n. {UserId}", user.Id);
                await MarkAsFailed(db, n, "Service non creato per l'utente n. " + n.Id, stoppingToken);
                return;
            }

            var requestId = LOLServiceHelper.GetRequestId(user.Guid, db, _options, service);
            if (requestId == null)
            {
                _logger.LogWarning("RequestId non ottenuto.");
                await MarkAsFailed(db, n, "RequestId non ottenuto", stoppingToken);
                return;
            }

            var sender = n.Operations.Senders.FirstOrDefault(a => a.Ar != true);
            if (sender == null)
            {
                _logger.LogWarning("Mittente non trovato per il destinatario n. {Id}", n.Id);
                await MarkAsFailed(db, n, "Mittente non trovato per il destinatario n." + n.Id, stoppingToken);
                return;
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

            n.worked = true;
            n.InProcessStep1 = false;

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

        _tracker.Untrack(item.NameId);
        _logger.LogInformation("🔓 Untrack completato per recipient ID {Id}", item.NameId);
    }

    private async Task MarkAsFailed(AppDbContext db, Recipients n, string msg, CancellationToken token)
    {
        n.InProcessStep1 = false;
        n.worked = true;
        n.CurrentState = (int)CurrentState.ErroreGenerico;
        n.Message = msg;
        n.Valid = false;

        using var scope = _scopeFactory.CreateScope();

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
