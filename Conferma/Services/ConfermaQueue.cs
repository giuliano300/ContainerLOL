using System.Collections.Concurrent;
using SharedLib.WsdlModels;

namespace Conferma.Services;

public interface IConfermaQueue
{
    /// <summary>
    /// Adds an item to the in-memory conferma queue.
    /// </summary>
    void Enqueue(ConfermaItem item);

    /// <summary>
    /// Attempts to remove the next item from the in-memory conferma queue.
    /// </summary>
    bool TryDequeue(out ConfermaItem? item);
}

public class ConfermaQueue : IConfermaQueue
{
    private readonly ConcurrentQueue<ConfermaItem> _queue = new();

    /// <summary>
    /// Adds an item to the in-memory conferma queue.
    /// </summary>
    public void Enqueue(ConfermaItem item) => _queue.Enqueue(item);

    /// <summary>
    /// Attempts to remove the next item from the in-memory conferma queue.
    /// </summary>
    public bool TryDequeue(out ConfermaItem? item) => _queue.TryDequeue(out item);
}
