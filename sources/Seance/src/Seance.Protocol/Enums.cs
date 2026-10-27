using System.Text.Json.Serialization;
using Seance.Protocol.Json;

namespace Seance.Protocol;

/// <summary>
/// A tri-state wire value (HPP section 4.1). <see cref="Unknown"/> is the zero value on purpose:
/// an uninitialised tri-state must never read as a definite negative (REQ-TRI-2).
/// </summary>
[JsonConverter(typeof(TriStateConverter))]
public enum TriState
{
    Unknown = 0,
    No,
    Yes,
}

/// <summary>
/// How much a signal can be trusted (HPP section 4.2). <see cref="Unreliable"/> is the zero value
/// so a forgotten assignment understates rather than overstates confidence.
/// </summary>
[JsonConverter(typeof(ReliabilityConverter))]
public enum Reliability
{
    Unreliable = 0,
    Indicative,
    Authoritative,
}

/// <summary>
/// The channel a surface represents (HPP section 4.3).
/// </summary>
[JsonConverter(typeof(SurfaceKindConverter))]
public enum SurfaceKind
{
    Unknown = 0,
    LocalSeat,
    Ssh,
    RemoteDesktop,
}

/// <summary>
/// Why the daemon published an event (HPP section 6.3, REQ-EVT-2).
/// </summary>
[JsonConverter(typeof(EventReasonConverter))]
public enum EventReason
{
    SurfaceConnected,
    SurfaceDisconnected,
    LockChanged,
    ConsoleChanged,
    IdleChanged,
    PinChanged,
}

/// <summary>
/// Why nothing can reach the owning user (HPP section 4.8).
/// </summary>
[JsonConverter(typeof(BlockedReasonConverter))]
public enum BlockedReason
{
    OffConsole,
    Locked,
    Absent,
    PinnedUnavailable,
}

/// <summary>
/// How the daemon established the identity of a connection (HPP section 4.7).
/// </summary>
[JsonConverter(typeof(VerifiedByConverter))]
public enum VerifiedBy
{
    Kernel,
    Token,
}
