using System.Collections.Concurrent;
using SharedLib.WsdlModels;

namespace Invio.Services;

public interface IInvioQueue
{
    /// <summary>
    /// Adds an item to the in-memory invio queue.
    /// </summary>
    void Enqueue(InvioItem item);

    /// <summary>
    /// Attempts to remove the next item from the in-memory invio queue.
    /// </summary>
    bool TryDequeue(out InvioItem? item);
}

public class InvioQueue : IInvioQueue
{
    private readonly ConcurrentQueue<InvioItem> _queue = new();

    /// <summary>
    /// Adds an item to the in-memory invio queue.
    /// </summary>
    public void Enqueue(InvioItem item) => _queue.Enqueue(item);

    /// <summary>
    /// Attempts to remove the next item from the in-memory invio queue.
    /// </summary>
    public bool TryDequeue(out InvioItem? item) => _queue.TryDequeue(out item);
}
