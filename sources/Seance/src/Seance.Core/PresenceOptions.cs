namespace Seance.Core;

/// <summary>
/// The knobs the spec leaves to the daemon.
/// </summary>
public sealed record PresenceOptions
{
    /// <summary>
    /// How long a session must have been idle before its return to input is worth an event
    /// (REQ-DEB-1, REQ-DEB-2). Lock, console, connect, disconnect and pin changes never wait.
    /// </summary>
    public TimeSpan IdleDebounce { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often to re-observe a provider that cannot push change signals.
    /// </summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How often to notice that a pin expired, on a provider that can push.
    /// </summary>
    public TimeSpan PinExpiryCheckInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How many events one connection may fall behind before the oldest are dropped. Events carry
    /// full snapshots (REQ-EVT-3), so a slow client loses reasons but never state.
    /// </summary>
    public int SubscriptionQueueCapacity { get; init; } = 64;
}
