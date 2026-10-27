using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seance.Protocol.Json;

/// <summary>
/// Base for the hand-written enum converters. The mapping lives in <see cref="WireNames"/> rather
/// than in a naming policy, so the wire format is written down once and reads the same everywhere.
/// </summary>
public abstract class WireEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    protected abstract string ToWire(T value);

    protected abstract bool TryParse(string text, out T value);

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException($"Expected a string value for {typeof(T).Name}.");
        var text = reader.GetString();
        if (text is null || !TryParse(text, out var value)) throw new JsonException($"'{text}' is not a valid {typeof(T).Name} value.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(ToWire(value));
}

public sealed class TriStateConverter : WireEnumConverter<TriState>
{
    protected override string ToWire(TriState value) => WireNames.Of(value);

    protected override bool TryParse(string text, out TriState value) => WireNames.TryParse(text, out value);
}

public sealed class ReliabilityConverter : WireEnumConverter<Reliability>
{
    protected override string ToWire(Reliability value) => WireNames.Of(value);

    protected override bool TryParse(string text, out Reliability value) => WireNames.TryParse(text, out value);
}

public sealed class SurfaceKindConverter : WireEnumConverter<SurfaceKind>
{
    protected override string ToWire(SurfaceKind value) => WireNames.Of(value);

    protected override bool TryParse(string text, out SurfaceKind value) => WireNames.TryParse(text, out value);
}

public sealed class EventReasonConverter : WireEnumConverter<EventReason>
{
    protected override string ToWire(EventReason value) => WireNames.Of(value);

    protected override bool TryParse(string text, out EventReason value) => WireNames.TryParse(text, out value);
}

public sealed class BlockedReasonConverter : WireEnumConverter<BlockedReason>
{
    protected override string ToWire(BlockedReason value) => WireNames.Of(value);

    protected override bool TryParse(string text, out BlockedReason value) => WireNames.TryParse(text, out value);
}

public sealed class VerifiedByConverter : WireEnumConverter<VerifiedBy>
{
    protected override string ToWire(VerifiedBy value) => WireNames.Of(value);

    protected override bool TryParse(string text, out VerifiedBy value) => WireNames.TryParse(text, out value);
}
