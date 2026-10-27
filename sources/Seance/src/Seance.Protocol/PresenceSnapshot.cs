using System.Text.Json.Serialization;
using Seance.Protocol.Json;

namespace Seance.Protocol;

/// <summary>
/// The result shape of presence.query and presence.subscribe (HPP section 6.2, REQ-MTH-5).
/// </summary>
public sealed record PresenceSnapshot
{
    public required IReadOnlyList<Surface> Surfaces { get; init; }

    public Pin? Pin { get; init; }

    public required PresenceCapabilities Capabilities { get; init; }

    [JsonConverter(typeof(Utc8601Converter))]
    public required DateTimeOffset GeneratedAt { get; init; }

    public required ServerInfo ServerInfo { get; init; }
}

/// <summary>
/// Informative identification of the daemon implementation. Clients must not change protocol
/// behaviour based on it (REQ-MTH-2).
/// </summary>
public sealed record ServerInfo
{
    public required string Name { get; init; }

    public required string Version { get; init; }
}
