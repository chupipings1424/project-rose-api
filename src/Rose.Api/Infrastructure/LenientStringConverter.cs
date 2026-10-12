using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rose.Api.Infrastructure;

/// <summary>
/// Lets a string property accept a JSON number or boolean too (<c>"amount": 1200.5</c> or
/// <c>"amount": "1,200.50"</c>), the way the original Python API accepted loose input.
/// </summary>
internal sealed class LenientStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan),
            JsonTokenType.True => "True",
            JsonTokenType.False => "False",
            _ => throw new JsonException($"Expected text but got {reader.TokenType}."),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}