using System.Collections.Concurrent;
using SharedLib.WsdlModels;

namespace Valorizza.Services;

public interface IValorizzaQueue
{
    /// <summary>
    /// Adds an item to the in-memory valorizza queue.
    /// </summary>
    void Enqueue(ValorizzaItem item);

    /// <summary>
    /// Attempts to remove the next item from the in-memory valorizza queue.
    /// </summary>
    bool TryDequeue(out ValorizzaItem? item);
}

public class ValorizzaQueue : IValorizzaQueue
{
    private readonly ConcurrentQueue<ValorizzaItem> _queue = new();

    /// <summary>
    /// Adds an item to the in-memory valorizza queue.
    /// </summary>
    public void Enqueue(ValorizzaItem item) => _queue.Enqueue(item);

    /// <summary>
    /// Attempts to remove the next item from the in-memory valorizza queue.
    /// </summary>
    public bool TryDequeue(out ValorizzaItem? item) => _queue.TryDequeue(out item);
}
