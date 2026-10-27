using System.Text.Json.Serialization;
using Seance.Protocol.Json;

namespace Seance.Protocol;

/// <summary>
/// The params of a presence.event notification (HPP section 6.3, REQ-EVT-2). Every event carries the
/// full current surface list rather than a delta (REQ-EVT-3), which makes a dropped notification
/// harmless.
/// </summary>
public sealed record PresenceEventParams
{
    public required EventReason Reason { get; init; }

    public required IReadOnlyList<Surface> Surfaces { get; init; }

    public Pin? Pin { get; init; }

    [JsonConverter(typeof(Utc8601Converter))]
    public required DateTimeOffset GeneratedAt { get; init; }
}
