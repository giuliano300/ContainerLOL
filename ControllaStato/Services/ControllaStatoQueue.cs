using System.Collections.Concurrent;
using SharedLib.WsdlModels;

namespace ControllaStato.Services;

public interface IControllaStatoQueue
{
    void Enqueue(ControllaStatoItem item);
    bool TryDequeue(out ControllaStatoItem item);
}

public class ControllaStatoQueue : IControllaStatoQueue
{
    private readonly ConcurrentQueue<ControllaStatoItem> _queue = new();

    public void Enqueue(ControllaStatoItem item) => _queue.Enqueue(item);
    public bool TryDequeue(out ControllaStatoItem item) => _queue.TryDequeue(out item);
}
