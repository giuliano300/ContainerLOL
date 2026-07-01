using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace SharedLib.Messaging;

/// <summary>
/// Starts RabbitMQ consumers with the standard single-message processing policy used by LOL workers.
/// </summary>
public static class RabbitConsumerHelper
{
    /// <summary>
    /// Declares the queue and consumes one message at a time with explicit ack/nack handling.
    /// </summary>
    public static Task StartSingleMessageConsumerAsync<TItem>(
        IModel channel,
        SemaphoreSlim semaphore,
        string queueName,
        ILogger logger,
        Func<string, TItem?> deserialize,
        Func<TItem, CancellationToken, Task> processItemAsync,
        CancellationToken stoppingToken)
        where TItem : class
    {
        // Ensure the RabbitMQ queue exists before starting consumption.
        channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false);

        // Process one message at a time so this worker never overlaps calls locally.
        channel.BasicQos(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, ea) =>
        {
            var acquired = await semaphore.WaitAsync(0, stoppingToken);
            if (!acquired)
            {
                logger.LogWarning("Processo già in esecuzione. Skip del messaggio.");
                channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
                return;
            }

            try
            {
                // Decode and deserialize the RabbitMQ payload before calling business code.
                var body = ea.Body.ToArray();
                var json = Encoding.UTF8.GetString(body);

                TItem? item;
                try
                {
                    item = deserialize(json);
                    if (item == null)
                    {
                        logger.LogWarning("Messaggio non valido: {Json}", json);
                        channel.BasicAck(ea.DeliveryTag, multiple: false);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Errore nella deserializzazione del messaggio RabbitMQ.");
                    channel.BasicAck(ea.DeliveryTag, multiple: false);
                    return;
                }

                // The processor owns the business outcome; successful return consumes the message.
                await processItemAsync(item, stoppingToken);
                channel.BasicAck(ea.DeliveryTag, multiple: false);
            }
            finally
            {
                semaphore.Release();
            }
        };

        channel.BasicConsume(queue: queueName, autoAck: false, consumer: consumer);
        return Task.CompletedTask;
    }
}
