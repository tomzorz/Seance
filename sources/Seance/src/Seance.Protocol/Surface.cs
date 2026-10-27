using System.Text.Json.Serialization;
using Seance.Protocol.Json;

namespace Seance.Protocol;

/// <summary>
/// One channel through which the owning user could currently interact with the machine
/// (HPP section 4.3).
/// </summary>
/// <remarks>
/// Locked, OnConsole and IdleMs describe independent facts and none of them may be derived from
/// another (REQ-SURF-3).
/// </remarks>
public sealed record Surface
{
    /// <summary>
    /// The channel this surface represents, derived from current platform signals (REQ-SURF-1).
    /// </summary>
    public required SurfaceKind Kind { get; init; }

    /// <summary>
    /// The platform-native session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// The session exists and is attached.
    /// </summary>
    public required bool Connected { get; init; }

    /// <summary>
    /// The session owns the local seat, so pixels this session draws are visible.
    /// </summary>
    public required TriState OnConsole { get; init; }

    /// <summary>
    /// The screen lock of this session is engaged.
    /// </summary>
    public required TriState Locked { get; init; }

    /// <summary>
    /// Milliseconds since input last arrived in this session, or null when unreadable.
    /// </summary>
    public long? IdleMs { get; init; }

    /// <summary>
    /// The remote peer, for the ssh and remoteDesktop kinds.
    /// </summary>
    public string? RemoteHost { get; init; }

    /// <summary>
    /// The remote protocol, for example ssh, rdp or vnc.
    /// </summary>
    public string? Protocol { get; init; }

    /// <summary>
    /// wayland, x11, or null when not applicable.
    /// </summary>
    public string? DisplayServer { get; init; }

    /// <summary>
    /// The display of this surface is powered.
    /// </summary>
    public TriState DisplayPowered { get; init; } = TriState.Unknown;

    /// <summary>
    /// A human-presence sensor detects a person. Usually unknown.
    /// </summary>
    public TriState HumanPresent { get; init; } = TriState.Unknown;

    /// <summary>
    /// Per-signal reliability for this surface.
    /// </summary>
    public required Confidence Confidence { get; init; }

    /// <summary>
    /// The UTC timestamp of the observation.
    /// </summary>
    [JsonConverter(typeof(Utc8601Converter))]
    public required DateTimeOffset ObservedAt { get; init; }
}
