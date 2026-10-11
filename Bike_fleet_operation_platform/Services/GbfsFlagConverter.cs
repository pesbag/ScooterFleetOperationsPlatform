using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bike_fleet_operation_platform.Services;

// GBFS flags may be booleans or 0/1, while our existing Kafka contract uses 0/1.
public class GbfsFlagConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.True => 1, JsonTokenType.False => 0, JsonTokenType.Null => null,
        JsonTokenType.Number when reader.TryGetInt32(out var value) && value is 0 or 1 => value,
        _ => throw new JsonException("Expected a GBFS boolean or 0/1 flag.")
    };
    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    { if (value.HasValue) writer.WriteNumberValue(value.Value); else writer.WriteNullValue(); }
}
