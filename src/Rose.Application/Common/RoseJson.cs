using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rose.Application.Common;

public static class RoseJson
{
    /// <summary>The data files (reminders.json, push-devices.json, vapid.json): camelCase, indented.</summary>
    public static readonly JsonSerializerOptions Files = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new IsoDateTimeOffsetConverter() },
    };

    /// <summary>Push payloads read by the service worker: compact camelCase.</summary>
    public static readonly JsonSerializerOptions Push = new(JsonSerializerDefaults.Web);
}

/// <summary>Writes instants exactly like JavaScript's <c>toISOString()</c>: <c>2026-10-07T08:30:00.000Z</c>.</summary>
public sealed class IsoDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
}