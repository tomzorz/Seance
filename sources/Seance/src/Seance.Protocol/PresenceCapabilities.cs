namespace Seance.Protocol;

/// <summary>
/// What the running platform code can actually deliver (HPP section 4.6). A member is true only when
/// the signal is genuinely available (REQ-CAP-1); a false member predicts permanent unknown or null
/// values for the matching surface fields.
/// </summary>
public sealed record PresenceCapabilities
{
    public required bool CanEnumerateSessions { get; init; }

    public required bool CanDetectRemoteDesktop { get; init; }

    public required bool CanReadIdle { get; init; }

    public required bool CanReadLockState { get; init; }

    public required bool HasHumanPresenceSensor { get; init; }
}
