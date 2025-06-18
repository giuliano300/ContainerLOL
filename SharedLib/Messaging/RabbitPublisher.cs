using Newtonsoft.Json;
using RabbitMQ.Client;
using System.Text;

namespace SharedLib.Messaging
{
    public class RabbitPublisher : IRabbitPublisher
    {
        private readonly IConnection _connection;

        public RabbitPublisher(IConnection connection)
        {
            _connection = connection;
        }

        public Task PublishAsync<T>(string queueName, T message)
        {
            using var channel = _connection.CreateModel();

            channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            var json = JsonConvert.SerializeObject(message);
            var body = Encoding.UTF8.GetBytes(json);

            var props = channel.CreateBasicProperties();
            props.Persistent = true;

            channel.BasicPublish(
                exchange: "",
                routingKey: queueName,
                basicProperties: props,
                body: body
            );

            return Task.CompletedTask;
        }
    }
}
