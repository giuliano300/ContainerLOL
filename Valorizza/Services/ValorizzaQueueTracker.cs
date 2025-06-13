using System.Collections.Concurrent;

public interface IValorizzaQueueTracker
{
    bool TryTrack(int id);
    void Untrack(int id);
}

public class ValorizzaQueueTracker : IValorizzaQueueTracker
{
    private readonly ConcurrentDictionary<int, bool> _ids = new();

    public bool TryTrack(int id) => _ids.TryAdd(id, true);
    public void Untrack(int id) => _ids.TryRemove(id, out _);
}
