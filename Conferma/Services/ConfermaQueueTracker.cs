using System.Collections.Concurrent;

public interface IConfermaQueueTracker
{
    bool TryTrack(int id);
    void Untrack(int id);
}

public class ConfermaQueueTracker : IConfermaQueueTracker
{
    private readonly ConcurrentDictionary<int, bool> _ids = new();

    public bool TryTrack(int id) => _ids.TryAdd(id, true);
    public void Untrack(int id) => _ids.TryRemove(id, out _);
}
