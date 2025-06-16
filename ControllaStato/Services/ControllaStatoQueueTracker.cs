using System.Collections.Concurrent;

public interface IControllaStatoQueueTracker
{
    bool TryTrack(int id);
    void Untrack(int id);
}

public class ControllaStatoQueueTracker : IControllaStatoQueueTracker
{
    private readonly ConcurrentDictionary<int, bool> _ids = new();

    public bool TryTrack(int id) => _ids.TryAdd(id, true);
    public void Untrack(int id) => _ids.TryRemove(id, out _);
}
