namespace SharedLib.Messaging
{
    public interface IRabbitPublisher
    {
        Task PublishAsync<T>(string queueName, T message);
    }
}
