using Seance.Protocol;

namespace Seance.Core;

/// <summary>
/// The fixed procedure of REQ-RCH-1: which surfaces can reach the owning user, in order.
/// </summary>
/// <remarks>
/// Pure by design. The daemon adds no policy here, and two daemons that observe the same snapshot,
/// the same pin and the same caller context have to produce the same object, so every step below is
/// the spec's step with the same number.
/// </remarks>
public static class ReachabilityCalculator
{
    public static Reachability Compute(
        IReadOnlyList<Surface> surfaces,
        Pin? pin,
        CallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(caller);

        // Step 1: every surface of the current snapshot is a candidate.
        IEnumerable<Surface> candidates = surfaces;

        // Step 2: the pin filter. Emptying the list here is its own blocked reason.
        if (pin is not null)
        {
            var pinned = candidates.Where(surface => surface.Kind == pin.Kind).ToList();
            if (pinned.Count == 0) return Blocked(BlockedReason.PinnedUnavailable);
            candidates = pinned;
        }

        // Step 3: drop what is not connected.
        // Step 4: drop seats and remote desktops that definitely cannot be seen. A value of
        // "unknown" removes nothing: exclusion needs a definite negative (REQ-TRI-1).
        var removedForConsole = false;
        var removedForLocked = false;
        var kept = new List<Surface>();

        foreach (var surface in candidates)
        {
            if (!surface.Connected) continue;

            if (surface.Kind is SurfaceKind.LocalSeat or SurfaceKind.RemoteDesktop)
            {
                var offConsole = surface.OnConsole == TriState.No;
                var locked = surface.Locked == TriState.Yes;
                if (offConsole || locked)
                {
                    removedForConsole |= offConsole;
                    removedForLocked |= locked;
                    continue;
                }
            }

            kept.Add(surface);
        }

        // Step 6: nothing left, so say what took the last candidate away.
        if (kept.Count == 0)
        {
            return Blocked(
                removedForConsole ? BlockedReason.OffConsole
                : removedForLocked ? BlockedReason.Locked
                : BlockedReason.Absent);
        }

        // Step 5: answer over the channel the request arrived on, first.
        var ordered = kept
            .OrderBy(surface => GroupRank(surface, caller))
            .ThenBy(surface => surface.IdleMs.HasValue ? 0 : 1)
            .ThenBy(surface => surface.IdleMs ?? 0L)
            .ThenBy(surface => surface.SessionId, StringComparer.Ordinal)
            .ToList();

        return new Reachability { Reachable = ordered, BlockedBy = null };
    }

    private static int GroupRank(Surface surface, CallerContext caller)
    {
        if (caller.OriginSessionId is not null &&
            string.Equals(surface.SessionId, caller.OriginSessionId, StringComparison.Ordinal))
        {
            return 0;
        }

        // An unclassified caller matches no kind. Pairing it with surfaces of kind "unknown" would
        // sort the least understood surface above the local seat, and every token-verified
        // connection reports an unknown origin (REQ-CTX-3), so this is the ordinary case.
        if (caller.OriginKind == SurfaceKind.Unknown) return 2;

        return surface.Kind == caller.OriginKind ? 1 : 2;
    }

    private static Reachability Blocked(BlockedReason reason) =>
        new() { Reachable = [], BlockedBy = reason };
}
