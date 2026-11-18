using System.Collections.Concurrent;

namespace YuChat.Realtime;

/// <summary>
/// In-memory presence and connection registry. Single-instance only: with more than one
/// server a shared backplane (Redis) would be required. Documented in the README.
/// </summary>
public sealed class PresenceTracker
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _connections = new();

    public bool AddConnection(Guid userId, string connectionId)
    {
        var connections = _connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
        var wasOffline = connections.IsEmpty;
        connections[connectionId] = 0;
        return wasOffline;
    }

    public bool RemoveConnection(Guid userId, string connectionId)
    {
        if (!_connections.TryGetValue(userId, out var connections))
        {
            return false;
        }

        connections.TryRemove(connectionId, out _);

        if (connections.IsEmpty)
        {
            _connections.TryRemove(userId, out _);
            return true;
        }

        return false;
    }

    public bool IsOnline(Guid userId) =>
        _connections.TryGetValue(userId, out var connections) && !connections.IsEmpty;

    public IReadOnlyList<Guid> OnlineUsers() =>
        _connections.Where(x => !x.Value.IsEmpty).Select(x => x.Key).ToArray();
}
