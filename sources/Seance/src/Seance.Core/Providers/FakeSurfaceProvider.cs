using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Seance.Protocol;

namespace Seance.Core.Providers;

/// <summary>
/// A provider whose surfaces are set from code. It is the driver for the Core tests and the
/// provider the daemon falls back to on a platform that has no real provider yet, which makes the
/// whole protocol exercisable end to end without a single OS call.
/// </summary>
/// <remarks>
/// This is synthetic input, not a mock: every line of engine logic downstream of it is the real
/// thing.
/// </remarks>
public sealed class FakeSurfaceProvider : ISurfaceProvider
{
    private readonly Channel<ProviderChangeSignal> _signals =
        Channel.CreateUnbounded<ProviderChangeSignal>(new UnboundedChannelOptions { SingleReader = true });

    private readonly object _gate = new();
    private IReadOnlyList<Surface> _surfaces;

    public FakeSurfaceProvider(params Surface[] surfaces) => _surfaces = surfaces;

    /// <summary>
    /// A machine that can see everything, so tests exercise the interesting states rather than
    /// permanent unknowns. Override for capability-specific cases.
    /// </summary>
    public ProviderCapabilities Capabilities { get; set; } = new()
    {
        Wire = new PresenceCapabilities
        {
            CanEnumerateSessions = true,
            CanDetectRemoteDesktop = true,
            CanReadIdle = true,
            CanReadLockState = true,
            HasHumanPresenceSensor = false,
        },
        IsPushCapable = true,
    };

    public IReadOnlyList<Surface> Current
    {
        get
        {
            lock (_gate) return _surfaces;
        }
    }

    /// <summary>
    /// Replaces the surface list and signals the change.
    /// </summary>
    public void Set(params Surface[] surfaces)
    {
        lock (_gate) _surfaces = surfaces;
        _signals.Writer.TryWrite(new ProviderChangeSignal("fake"));
    }

    /// <summary>
    /// Replaces one surface, matched by session id, and signals the change.
    /// </summary>
    public void Update(string sessionId, Func<Surface, Surface> change)
    {
        lock (_gate)
        {
            var updated = new List<Surface>(_surfaces.Count);
            foreach (var surface in _surfaces)
                updated.Add(surface.SessionId == sessionId ? change(surface) : surface);
            _surfaces = updated;
        }

        _signals.Writer.TryWrite(new ProviderChangeSignal("fake"));
    }

    public Task<IReadOnlyList<Surface>> SnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Current);
    }

    public async IAsyncEnumerable<ProviderChangeSignal> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await _signals.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            while (_signals.Reader.TryRead(out var signal))
                yield return signal;
        }
    }
}
