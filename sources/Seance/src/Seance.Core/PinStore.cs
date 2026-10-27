using Seance.Protocol;

namespace Seance.Core;

/// <summary>
/// Holds the active pin (HPP section 5.4). Pins are the owning user's correction for wrong
/// heuristics, so they are not optional (REQ-PIN-1).
/// </summary>
/// <remarks>
/// Expiry is evaluated on read rather than on a timer, so a pin can never outlive its expiry
/// timestamp no matter what the daemon is doing (REQ-PIN-4). The engine still polls
/// <see cref="Current"/> so that an expiry publishes a pinChanged event.
/// </remarks>
public sealed class PinStore(TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private Pin? _pin;

    /// <summary>
    /// The pin in force right now, or null when none is or an expired one was.
    /// </summary>
    public Pin? Current
    {
        get
        {
            lock (_gate)
            {
                if (_pin is { ExpiresAt: { } expiry } && expiry <= timeProvider.GetUtcNow()) _pin = null;
                return _pin;
            }
        }
    }

    /// <summary>
    /// Sets a pin, replacing any active one (REQ-PIN-5).
    /// </summary>
    /// <param name="kind">The surface kind to pin to. Not <see cref="SurfaceKind.Unknown"/>.</param>
    /// <param name="duration">How long the pin lasts, or null to last until removed (REQ-MTH-3).</param>
    public Pin Set(SurfaceKind kind, TimeSpan? duration)
    {
        if (kind == SurfaceKind.Unknown) throw new ArgumentOutOfRangeException(nameof(kind), kind, "A pin needs a real surface kind.");
        if (duration is { } span && span <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration), duration, "A pin duration must be positive.");

        var now = timeProvider.GetUtcNow();
        var pin = new Pin
        {
            Kind = kind,
            PinnedAt = now,
            ExpiresAt = duration is { } length ? now + length : null,
        };

        lock (_gate) _pin = pin;
        return pin;
    }

    /// <summary>
    /// Removes the active pin, if any.
    /// </summary>
    public void Clear()
    {
        lock (_gate) _pin = null;
    }
}
