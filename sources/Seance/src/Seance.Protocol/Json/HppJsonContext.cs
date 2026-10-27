using System.Text.Json.Serialization;

namespace Seance.Protocol.Json;

/// <summary>
/// The source-generated serializer for every HPP wire type. Reflection-based serialization is off
/// the table: the daemon publishes NativeAOT, and retrofitting source generation later is painful.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PresenceSnapshot))]
[JsonSerializable(typeof(Surface))]
[JsonSerializable(typeof(IReadOnlyList<Surface>))]
[JsonSerializable(typeof(Confidence))]
[JsonSerializable(typeof(Pin))]
[JsonSerializable(typeof(PresenceCapabilities))]
[JsonSerializable(typeof(ServerInfo))]
[JsonSerializable(typeof(CallerContext))]
[JsonSerializable(typeof(AncestryEntry))]
[JsonSerializable(typeof(Reachability))]
[JsonSerializable(typeof(PresenceEventParams))]
[JsonSerializable(typeof(PinParams))]
[JsonSerializable(typeof(AuthenticateParams))]
[JsonSerializable(typeof(AuthenticateResult))]
[JsonSerializable(typeof(Acknowledgment))]
public sealed partial class HppJsonContext : JsonSerializerContext;
