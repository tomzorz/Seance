using Seance.Protocol;

namespace Seance.Core.Providers;

/// <summary>
/// Builds synthetic surfaces. Used by <see cref="FakeSurfaceProvider"/> and by the tests, which feed
/// real engine logic with scripted input rather than mocking anything out.
/// </summary>
public static class FakeSurfaces
{
    public static Surface LocalSeat(
        string sessionId = "1",
        TriState onConsole = TriState.Yes,
        TriState locked = TriState.No,
        long? idleMs = 0,
        bool connected = true,
        string? displayServer = null,
        DateTimeOffset? observedAt = null) =>
        Build(SurfaceKind.LocalSeat, sessionId, connected, onConsole, locked, idleMs, null, null, displayServer, observedAt);

    public static Surface Ssh(
        string sessionId = "2",
        long? idleMs = 0,
        bool connected = true,
        string? remoteHost = "10.0.0.12",
        DateTimeOffset? observedAt = null) =>
        Build(SurfaceKind.Ssh, sessionId, connected, TriState.No, TriState.Unknown, idleMs, remoteHost, "ssh", null, observedAt);

    public static Surface RemoteDesktop(
        string sessionId = "3",
        TriState onConsole = TriState.No,
        TriState locked = TriState.No,
        long? idleMs = 0,
        bool connected = true,
        string? remoteHost = "10.0.0.13",
        string? protocol = "rdp",
        DateTimeOffset? observedAt = null) =>
        Build(SurfaceKind.RemoteDesktop, sessionId, connected, onConsole, locked, idleMs, remoteHost, protocol, null, observedAt);

    public static Surface Unknown(
        string sessionId = "4",
        long? idleMs = null,
        bool connected = true,
        DateTimeOffset? observedAt = null) =>
        Build(SurfaceKind.Unknown, sessionId, connected, TriState.Unknown, TriState.Unknown, idleMs, null, null, null, observedAt);

    private static Surface Build(
        SurfaceKind kind,
        string sessionId,
        bool connected,
        TriState onConsole,
        TriState locked,
        long? idleMs,
        string? remoteHost,
        string? protocol,
        string? displayServer,
        DateTimeOffset? observedAt) => new()
        {
            Kind = kind,
            SessionId = sessionId,
            Connected = connected,
            OnConsole = onConsole,
            Locked = locked,
            IdleMs = idleMs,
            RemoteHost = remoteHost,
            Protocol = protocol,
            DisplayServer = displayServer,
            DisplayPowered = TriState.Unknown,
            HumanPresent = TriState.Unknown,
            Confidence = new Confidence
            {
                // The fake is the source of truth for its own state, so a value it does state is
                // authoritative and a value it leaves unknown is unreliable (REQ-CONF-2).
                Locked = Rate(locked),
                OnConsole = Rate(onConsole),
                Idle = idleMs.HasValue ? Reliability.Authoritative : Reliability.Unreliable,
                Kind = kind == SurfaceKind.Unknown ? Reliability.Unreliable : Reliability.Authoritative,
            },
            ObservedAt = observedAt ?? DateTimeOffset.UnixEpoch,
        };

    private static Reliability Rate(TriState value) =>
        value == TriState.Unknown ? Reliability.Unreliable : Reliability.Authoritative;
}
