using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecuperaDocumentoFinale.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SharedLib.Config;
using SharedLib.Db;
using SharedLib.Models;
using SharedLib.Utils;
using SharedLib.WsdlModels;
using System.Text;
using System.Text.Json;
using System.Threading;

public class RecuperaDocumentoFinaleProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LolServiceOptions _options;
    private readonly ILogger<RecuperaDocumentoFinaleProcessor> _logger;
    private readonly IRecuperaDocumentoFinaleQueueTracker _tracker;
    private readonly IConnection _connection;
    private IModel _channel;

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private const string QueueName = "recupera_documento_finale_lol_queue";

    public RecuperaDocumentoFinaleProcessor(
        IServiceScopeFactory scopeFactory,
        IOptions<LolServiceOptions> options,
        ILogger<RecuperaDocumentoFinaleProcessor> logger,
        IRecuperaDocumentoFinaleQueueTracker tracker,
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

                ConfermaItem? item = null;
                try
                {
                    item = JsonSerializer.Deserialize<ConfermaItem>(json);
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

        var n = await db.Recipients
            .Include(x => x.Operations).ThenInclude(o => o.Users)
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

            var dcs = await service.RecuperaDocumentoFinaleAsync(n.Code);
            var result = dcs.RecuperaDocumentoFinaleResult;

            if (result.CEResult.Type == "I")
                n.PathRecoveryFile = Convert.ToBase64String(result.Documento.Contenuto);

            db.RecipientWorks.Add(new RecipientWorks
            {
                Message = result.CEResult.Description,
                RecipientId = n.Id,
                WorkDate = DateTime.UtcNow,
                WorkStatus = (int)WorkStatus.InviatoRecuperaDocumentoFinale
            });

            n.worked = true;
            n.InProcessStep4 = false;

            await db.SaveChangesAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Errore durante il recupero documento finale");

            db.RecipientWorks.Add(new RecipientWorks
            {
                Message = ex.Message,
                RecipientId = n.Id,
                WorkDate = DateTime.UtcNow,
                WorkStatus = (int)WorkStatus.InviatoRecuperaDocumentoFinale
            });

            await db.SaveChangesAsync(stoppingToken);
        }

        _tracker.Untrack(n.Id);
    }
}
