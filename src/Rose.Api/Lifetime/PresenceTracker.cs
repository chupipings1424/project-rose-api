namespace Rose.Api.Lifetime;

/// <summary>Counts the open ROSE pages (each one holds a presence stream open).</summary>
public sealed class PresenceTracker(TimeProvider clock)
{
    private readonly Lock gate = new();
    private int connections;
    private DateTimeOffset? idleSince;

    public int Connections
    {
        get { lock (gate) return connections; }
    }

    /// <summary>Registers one open page; dispose the result when it disconnects.</summary>
    public IDisposable Connect()
    {
        lock (gate)
        {
            connections++;
            idleSince = null;
        }
        return new Connection(this);
    }

    /// <summary>
    /// How long no page has been connected. Null while a page is open, and before the
    /// first page ever connected (so a slow browser start never shuts ROSE down).
    /// </summary>
    public TimeSpan? IdleFor()
    {
        lock (gate)
            return idleSince is { } since ? clock.GetUtcNow() - since : null;
    }

    private void Disconnect()
    {
        lock (gate)
        {
            connections--;
            if (connections == 0) idleSince = clock.GetUtcNow();
        }
    }

    private sealed class Connection(PresenceTracker owner) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) owner.Disconnect();
        }
    }
}