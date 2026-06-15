using Conferma.Services;
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

public class ConfermaProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LolServiceOptions _options;
    private readonly ILogger<ConfermaProcessor> _logger;
    private readonly IConfermaQueueTracker _tracker;
    private readonly IConnection _connection;
    private IModel _channel;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private const string QueueName = "conferma_lol_queue";

    public ConfermaProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<LolServiceOptions> options,
        ILogger<ConfermaProcessor> logger,
        IConfermaQueueTracker tracker,
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

                ConfermaItem? item;
                try
                {
                    item = JsonConvert.DeserializeObject<ConfermaItem>(json);
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

    private async Task ProcessItemAsync(ConfermaItem item, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            var n = await db.Recipients
                .Include(x => x.Operations).ThenInclude(o => o.Users)
                .FirstOrDefaultAsync(x => x.Id == item.NameId, stoppingToken);

            if (n == null)
            {
                _logger.LogError("Recipient non trovato.");
                return;
            }

            if (n.CurrentState != (int)CurrentState.documentoValidato)
            {
                _logger.LogWarning(
                    "Recipient {Id} già processato. Stato attuale: {State}",
                    n.Id,
                    n.CurrentState);

                n.InProcessStep3 = false;
                n.worked = true;
                await db.SaveChangesAsync(stoppingToken);

                return;
            }

            var user = n.Operations.Users;
            var service = LOLServiceHelper.GetNewServiceLOL(user, _options);

            var richieste = new[] { new Richiesta { IDRichiesta = n.RequestId } };
            var preConferma = await service.PreConfermaAsync(richieste, true);
            var result = preConferma.PreConfermaResult;

            if (result.CEResult.Type == "I" && result.DestinatariLettera?.Length > 0)
            {
                n.CurrentState = (int)CurrentState.presaInCarico;
                n.Code = result.DestinatariLettera[0].IdRicevuta;
                n.Message = "Presa in Carico Poste";

                var dcs = await service.RecuperaDocumentoFinaleAsync(n.Code);
                if (dcs.RecuperaDocumentoFinaleResult.CEResult.Type == "I")
                    n.PathRecoveryFile = Convert.ToBase64String(dcs.RecuperaDocumentoFinaleResult.Documento.Contenuto);

                _logger.LogInformation($"Conferma recipient n.{n.Id}");
            }
            else
            {
                n.Message = result.CEResult.Description;
                n.Valid = false;
                n.CurrentState = (int)CurrentState.ErroreConfirm;

                _logger.LogWarning($"Errore conferma recipient n.{n.Id}");
            }

            n.InProcessStep3 = false;
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

            var n = await db.Recipients.FirstOrDefaultAsync(x => x.Id == item.NameId, stoppingToken);
            if (n != null)
            {
                n.CurrentState = (int)CurrentState.ErroreGenerico;
                n.Message = ex.Message;
                n.Valid = false;
                n.InProcessStep3 = false;
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
        }
        finally
        {
            _tracker.Untrack(item.NameId);
        }
    }
}
