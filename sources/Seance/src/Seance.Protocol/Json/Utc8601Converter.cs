using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seance.Protocol.Json;

/// <summary>
/// Writes timestamps the way the spec's examples show them: UTC, millisecond precision, trailing Z.
/// The default <see cref="DateTimeOffset"/> format emits a numeric offset instead, which is still
/// ISO 8601 but reads as a different dialect to anyone eyeballing the wire.
/// </summary>
public sealed class Utc8601Converter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTimeOffset().ToUniversalTime();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
}
