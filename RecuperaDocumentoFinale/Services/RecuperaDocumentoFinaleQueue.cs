using System.Collections.Concurrent;
using SharedLib.WsdlModels;

namespace RecuperaDocumentoFinale.Services;

public interface IRecuperaDocumentoFinaleQueue
{
    /// <summary>
    /// Adds an item to the in-memory recupera documento finale queue.
    /// </summary>
    void Enqueue(ConfermaItem item);

    /// <summary>
    /// Attempts to remove the next item from the in-memory recupera documento finale queue.
    /// </summary>
    bool TryDequeue(out ConfermaItem? item);
}

public class RecuperaDocumentoFinaleQueue : IRecuperaDocumentoFinaleQueue
{
    private readonly ConcurrentQueue<ConfermaItem> _queue = new();

    /// <summary>
    /// Adds an item to the in-memory recupera documento finale queue.
    /// </summary>
    public void Enqueue(ConfermaItem item) => _queue.Enqueue(item);

    /// <summary>
    /// Attempts to remove the next item from the in-memory recupera documento finale queue.
    /// </summary>
    public bool TryDequeue(out ConfermaItem? item) => _queue.TryDequeue(out item);
}
