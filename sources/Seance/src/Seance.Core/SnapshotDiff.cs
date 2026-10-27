using Seance.Protocol;

namespace Seance.Core;

/// <summary>
/// Turns two consecutive observations into the list of event reasons the daemon publishes
/// (HPP sections 5.3 and 6.3).
/// </summary>
/// <remarks>
/// Deriving reasons here rather than in each provider is what keeps two platforms from disagreeing
/// about what counts as a lock change.
/// </remarks>
public static class SnapshotDiff
{
    /// <summary>
    /// Compares two surface lists and returns the reasons to publish, in a fixed order.
    /// </summary>
    /// <param name="previous">The previously published surface list.</param>
    /// <param name="current">The freshly observed surface list.</param>
    /// <param name="idleDebounce">
    /// How long a session must have been idle for its return to input to be worth an event
    /// (REQ-DEB-2, REQ-EVT-5).
    /// </param>
    public static IReadOnlyList<EventReason> Compare(
        IReadOnlyList<Surface> previous,
        IReadOnlyList<Surface> current,
        TimeSpan idleDebounce)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var before = Index(previous);
        var after = Index(current);

        var disconnected = false;
        var connected = false;
        var consoleChanged = false;
        var lockChanged = false;
        var idleChanged = false;

        foreach (var (sessionId, old) in before)
        {
            if (!after.TryGetValue(sessionId, out var now))
            {
                disconnected |= old.Connected;
                continue;
            }

            if (old.Connected && !now.Connected) disconnected = true;
            if (!old.Connected && now.Connected) connected = true;

            // A surface that came or went reports that, not the fields that went with it.
            if (old.Connected != now.Connected) continue;

            if (old.OnConsole != now.OnConsole) consoleChanged = true;
            if (old.Locked != now.Locked) lockChanged = true;
            if (ReturnedFromIdle(old, now, idleDebounce)) idleChanged = true;
        }

        foreach (var (sessionId, now) in after)
        {
            if (!before.ContainsKey(sessionId)) connected |= now.Connected;
        }

        var reasons = new List<EventReason>(5);
        if (disconnected) reasons.Add(EventReason.SurfaceDisconnected);
        if (connected) reasons.Add(EventReason.SurfaceConnected);
        if (consoleChanged) reasons.Add(EventReason.ConsoleChanged);
        if (lockChanged) reasons.Add(EventReason.LockChanged);
        if (idleChanged) reasons.Add(EventReason.IdleChanged);
        return reasons;
    }

    /// <summary>
    /// Input arrived in a session that had been idle for at least the debounce window. Rising idle
    /// time never qualifies: it is a counter that moves every second, so "the value changed" cannot
    /// be a trigger (REQ-EVT-5).
    /// </summary>
    private static bool ReturnedFromIdle(Surface old, Surface now, TimeSpan idleDebounce) =>
        old.IdleMs is { } before &&
        now.IdleMs is { } after &&
        after < before &&
        before >= (long)idleDebounce.TotalMilliseconds;

    private static Dictionary<string, Surface> Index(IReadOnlyList<Surface> surfaces)
    {
        var index = new Dictionary<string, Surface>(surfaces.Count, StringComparer.Ordinal);
        foreach (var surface in surfaces) index.TryAdd(surface.SessionId, surface);
        return index;
    }
}
