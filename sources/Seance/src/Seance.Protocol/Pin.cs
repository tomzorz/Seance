using System.Text.Json.Serialization;
using Seance.Protocol.Json;

namespace Seance.Protocol;

/// <summary>
/// An override, set by the owning user, that replaces derived presence state in reachability until
/// the pin expires or is removed (HPP section 4.5).
/// </summary>
public sealed record Pin
{
    /// <summary>
    /// The pinned surface kind. Never SurfaceKind.Unknown (REQ-MTH-4).
    /// </summary>
    public required SurfaceKind Kind { get; init; }

    /// <summary>
    /// The UTC timestamp at which the pin was set.
    /// </summary>
    [JsonConverter(typeof(Utc8601Converter))]
    public required DateTimeOffset PinnedAt { get; init; }

    /// <summary>
    /// The UTC expiry timestamp, or null when the pin lasts until removed.
    /// </summary>
    [JsonConverter(typeof(Utc8601Converter))]
    public DateTimeOffset? ExpiresAt { get; init; }
}
