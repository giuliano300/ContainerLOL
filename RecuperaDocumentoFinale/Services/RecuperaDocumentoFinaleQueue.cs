using System.Collections.Concurrent;
using SharedLib.WsdlModels;

namespace RecuperaDocumentoFinale.Services;

public interface IRecuperaDocumentoFinaleQueue
{
    void Enqueue(ConfermaItem item);
    bool TryDequeue(out ConfermaItem item);
}

public class RecuperaDocumentoFinaleQueue : IRecuperaDocumentoFinaleQueue
{
    private readonly ConcurrentQueue<ConfermaItem> _queue = new();

    public void Enqueue(ConfermaItem item) => _queue.Enqueue(item);
    public bool TryDequeue(out ConfermaItem item) => _queue.TryDequeue(out item);
}
