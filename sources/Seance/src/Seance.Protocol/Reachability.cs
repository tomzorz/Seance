namespace Seance.Protocol;

/// <summary>
/// The daemon's computed, ordered view of which surfaces can reach the owning user now
/// (HPP sections 4.8 and 5.5). Deterministic: the same snapshot, pin and caller context always
/// produce the same object (REQ-RCH-1).
/// </summary>
public sealed record Reachability
{
    /// <summary>
    /// Surfaces ordered from most to least likely to reach the user. Can be empty (REQ-RCH-2).
    /// </summary>
    public required IReadOnlyList<Surface> Reachable { get; init; }

    /// <summary>
    /// Null when <see cref="Reachable"/> is not empty, otherwise why nothing can reach the user.
    /// </summary>
    public BlockedReason? BlockedBy { get; init; }
}
