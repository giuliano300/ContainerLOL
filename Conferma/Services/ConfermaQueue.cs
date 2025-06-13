using System.Collections.Concurrent;
using SharedLib.WsdlModels;

namespace Conferma.Services;

public interface IConfermaQueue
{
    void Enqueue(ConfermaItem item);
    bool TryDequeue(out ConfermaItem item);
}

public class ConfermaQueue : IConfermaQueue
{
    private readonly ConcurrentQueue<ConfermaItem> _queue = new();

    public void Enqueue(ConfermaItem item) => _queue.Enqueue(item);
    public bool TryDequeue(out ConfermaItem item) => _queue.TryDequeue(out item);
}
