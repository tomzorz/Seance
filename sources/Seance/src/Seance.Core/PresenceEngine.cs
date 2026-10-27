using Seance.Core.Providers;
using Seance.Protocol;

namespace Seance.Core;

/// <summary>
/// The daemon's brain: it observes the provider, decides what is worth publishing, and answers the
/// protocol's questions. Everything platform-specific sits behind <see cref="ISurfaceProvider"/> and
/// everything transport-specific sits above this class.
/// </summary>
public sealed class PresenceEngine
{
    private readonly ISurfaceProvider _provider;
    private readonly PinStore _pins;
    private readonly TimeProvider _timeProvider;
    private readonly PresenceOptions _options;
    private readonly ServerInfo _serverInfo;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _gate = new();
    private readonly List<PresenceSubscription> _subscriptions = [];

    private IReadOnlyList<Surface> _surfaces = [];
    private Pin? _publishedPin;

    public PresenceEngine(
        ISurfaceProvider provider,
        PinStore pins,
        TimeProvider timeProvider,
        PresenceOptions? options = null,
        ServerInfo? serverInfo = null)
    {
        _provider = provider;
        _pins = pins;
        _timeProvider = timeProvider;
        _options = options ?? new PresenceOptions();
        _serverInfo = serverInfo ?? DefaultServerInfo();
    }

    public PresenceOptions Options => _options;

    /// <summary>
    /// Observes the provider once and adopts the result without publishing anything. Used at
    /// startup, so the first client to connect does not receive a surfaceConnected event for
    /// surfaces that were there all along.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var observed = await _provider.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _surfaces = observed;
            _publishedPin = _pins.Current;
        }
    }

    /// <summary>
    /// Observes the provider and publishes an event for each change that qualifies.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var observed = await _provider.SnapshotAsync(cancellationToken).ConfigureAwait(false);
            Apply(observed);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Publishes a pinChanged event if the active pin expired since the last publish (REQ-PIN-4).
    /// </summary>
    public void CheckPinExpiry() => PublishPinChangeIfNeeded();

    /// <summary>
    /// The current snapshot: the result of presence.query.
    /// </summary>
    public PresenceSnapshot Snapshot()
    {
        var pin = _pins.Current;
        lock (_gate) return BuildSnapshot(pin);
    }

    /// <summary>
    /// The result of presence.reach for one caller.
    /// </summary>
    public Reachability Reach(CallerContext caller)
    {
        var pin = _pins.Current;
        IReadOnlyList<Surface> surfaces;
        lock (_gate) surfaces = _surfaces;
        return ReachabilityCalculator.Compute(surfaces, pin, caller);
    }

    /// <summary>
    /// Sets a pin and publishes the change without debounce (REQ-DEB-3, REQ-PIN-5).
    /// </summary>
    public Pin SetPin(SurfaceKind kind, TimeSpan? duration)
    {
        var pin = _pins.Set(kind, duration);
        PublishPinChangeIfNeeded();
        return pin;
    }

    /// <summary>
    /// Removes the active pin and publishes the change without debounce.
    /// </summary>
    public void RemovePin()
    {
        _pins.Clear();
        PublishPinChangeIfNeeded();
    }

    /// <summary>
    /// Starts an event stream and returns the current snapshot in the same operation, so no
    /// transition can fall between a query and a following subscribe (REQ-MTH-5).
    /// </summary>
    public (PresenceSubscription Subscription, PresenceSnapshot Snapshot) Subscribe()
    {
        var pin = _pins.Current;
        var subscription = new PresenceSubscription(_options.SubscriptionQueueCapacity, Unsubscribe);

        lock (_gate)
        {
            _subscriptions.Add(subscription);
            return (subscription, BuildSnapshot(pin));
        }
    }

    private PresenceSnapshot BuildSnapshot(Pin? pin) => new()
    {
        Surfaces = _surfaces,
        Pin = pin,
        Capabilities = _provider.Capabilities.Wire,
        GeneratedAt = _timeProvider.GetUtcNow(),
        ServerInfo = _serverInfo,
    };

    private void Apply(IReadOnlyList<Surface> observed)
    {
        // Read the pin before publishing anything, so events carry the pin that is actually in
        // force rather than the one that was in force at the last pin change.
        var pin = _pins.Current;

        lock (_gate)
        {
            var reasons = new List<EventReason>(SnapshotDiff.Compare(_surfaces, observed, _options.IdleDebounce));
            _surfaces = observed;

            if (_publishedPin != pin)
            {
                _publishedPin = pin;
                reasons.Add(EventReason.PinChanged);
            }

            foreach (var reason in reasons) Publish(reason);
        }
    }

    private void PublishPinChangeIfNeeded()
    {
        var pin = _pins.Current;
        lock (_gate)
        {
            if (_publishedPin == pin) return;
            _publishedPin = pin;
            Publish(EventReason.PinChanged);
        }
    }

    private void Publish(EventReason reason)
    {
        var notification = new PresenceEventParams
        {
            Reason = reason,
            Surfaces = _surfaces,
            Pin = _publishedPin,
            GeneratedAt = _timeProvider.GetUtcNow(),
        };

        foreach (var subscription in _subscriptions) subscription.Publish(notification);
    }

    private void Unsubscribe(PresenceSubscription subscription)
    {
        lock (_gate) _subscriptions.Remove(subscription);
    }

    private static ServerInfo DefaultServerInfo()
    {
        var version = typeof(PresenceEngine).Assembly.GetName().Version;
        return new ServerInfo
        {
            Name = "seance",
            Version = version?.ToString(3) ?? "0.0.0",
        };
    }
}
