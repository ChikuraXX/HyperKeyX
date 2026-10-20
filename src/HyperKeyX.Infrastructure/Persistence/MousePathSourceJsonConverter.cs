using System.Text.Json;
using System.Text.Json.Serialization;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Infrastructure.Persistence;

/// <summary>显式创建冻结源块，不依赖可变集合的反序列化构造约定。</summary>
internal sealed class MousePathSourceJsonConverter : JsonConverter<MousePathSource>
{
    public override MousePathSource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var json = JsonDocument.ParseValue(ref reader);
        var points = PathJsonContract.Required(json.RootElement, "points");
        if (points.ValueKind != JsonValueKind.Array || points.GetArrayLength() > MousePathLimits.MaximumMacroPoints)
            throw new JsonException("Invalid or oversized mouse path source.");
        return new MousePathSource(points.Deserialize<MousePathPoint[]>(options) ?? throw new JsonException("Missing points."));
    }

    public override void Write(Utf8JsonWriter writer, MousePathSource value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("points");
        JsonSerializer.Serialize(writer, value.Points, options);
        writer.WriteEndObject();
    }
}
