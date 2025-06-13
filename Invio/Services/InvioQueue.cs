using System.Collections.Concurrent;
using SharedLib.WsdlModels;

namespace Invio.Services;

public interface IInvioQueue
{
    void Enqueue(InvioItem item);
    bool TryDequeue(out InvioItem item);
}

public class InvioQueue : IInvioQueue
{
    private readonly ConcurrentQueue<InvioItem> _queue = new();

    public void Enqueue(InvioItem item) => _queue.Enqueue(item);
    public bool TryDequeue(out InvioItem item) => _queue.TryDequeue(out item);
}
