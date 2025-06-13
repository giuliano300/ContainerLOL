using System.Collections.Concurrent;
using SharedLib.WsdlModels;

namespace Valorizza.Services;

public interface IValorizzaQueue
{
    void Enqueue(ValorizzaItem item);
    bool TryDequeue(out ValorizzaItem item);
}

public class ValorizzaQueue : IValorizzaQueue
{
    private readonly ConcurrentQueue<ValorizzaItem> _queue = new();

    public void Enqueue(ValorizzaItem item) => _queue.Enqueue(item);
    public bool TryDequeue(out ValorizzaItem item) => _queue.TryDequeue(out item);
}
