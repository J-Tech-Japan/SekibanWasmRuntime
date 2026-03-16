using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Sekiban.Dcb.Primitives;

namespace Sekiban.Dcb.WasmRuntime.Host;

internal sealed class ProjectionInstanceStore : IDisposable
{
    private readonly ConcurrentDictionary<string, StoredProjectionInstance> _instances = new(StringComparer.Ordinal);
    private readonly TimeSpan _idleTimeout;
    private readonly Func<DateTimeOffset> _clock;

    public ProjectionInstanceStore()
        : this(TimeSpan.FromMinutes(30))
    {
    }

    internal ProjectionInstanceStore(TimeSpan idleTimeout, Func<DateTimeOffset>? clock = null)
    {
        _idleTimeout = idleTimeout;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string Add(IPrimitiveProjectionInstance instance)
    {
        CleanupExpiredInstances();

        var instanceId = Guid.NewGuid().ToString();
        _instances[instanceId] = new StoredProjectionInstance(instance, _clock());
        return instanceId;
    }

    public bool TryGet(
        string instanceId,
        [NotNullWhen(true)] out IPrimitiveProjectionInstance? instance)
    {
        CleanupExpiredInstances();

        if (_instances.TryGetValue(instanceId, out var entry))
        {
            entry.Touch(_clock());
            instance = entry.Instance;
            return true;
        }

        instance = null;
        return false;
    }

    public bool Remove(string instanceId)
    {
        if (_instances.TryRemove(instanceId, out var entry))
        {
            entry.Dispose();
            return true;
        }

        return false;
    }

    internal int CleanupExpiredInstances()
    {
        if (_instances.IsEmpty)
        {
            return 0;
        }

        var removed = 0;
        var threshold = _clock().Subtract(_idleTimeout);
        foreach (var pair in _instances)
        {
            if (pair.Value.LastAccessUtc > threshold)
            {
                continue;
            }

            if (_instances.TryRemove(pair.Key, out var expired))
            {
                expired.Dispose();
                removed++;
            }
        }

        return removed;
    }

    public void Dispose()
    {
        foreach (var instanceId in _instances.Keys)
        {
            Remove(instanceId);
        }
    }

    private sealed class StoredProjectionInstance : IDisposable
    {
        private long _lastAccessUtcTicks;

        public StoredProjectionInstance(IPrimitiveProjectionInstance instance, DateTimeOffset createdAt)
        {
            Instance = instance;
            _lastAccessUtcTicks = createdAt.UtcTicks;
        }

        public IPrimitiveProjectionInstance Instance { get; }

        public DateTimeOffset LastAccessUtc =>
            new(Interlocked.Read(ref _lastAccessUtcTicks), TimeSpan.Zero);

        public void Touch(DateTimeOffset accessedAt)
        {
            Interlocked.Exchange(ref _lastAccessUtcTicks, accessedAt.UtcTicks);
        }

        public void Dispose()
        {
            Instance.Dispose();
        }
    }
}
