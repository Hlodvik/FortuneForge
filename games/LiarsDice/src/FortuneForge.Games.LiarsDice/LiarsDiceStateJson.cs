using System.Text.Json;
using System.Text.Json.Serialization;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Dice;

namespace FortuneForge.Games.LiarsDice;

/// <summary>Reads both historical object dice and numeric dice without defaulting readonly values to zero.</summary>
public static class LiarsDiceStateJson
{
    public static JsonSerializerOptions Options { get; } = Create();
    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(RoundAuditCapture.Json);
        options.Converters.Add(new DieConverter());
        return options;
    }
    private sealed class DieConverter : JsonConverter<DieValue>
    {
        public override DieValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var json = JsonDocument.ParseValue(ref reader);
            var root = json.RootElement;
            return new DieValue(root.ValueKind == JsonValueKind.Number ? root.GetInt32() : root.GetProperty("value").GetInt32());
        }
        public override void Write(Utf8JsonWriter writer, DieValue value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("value", value.Value);
            writer.WriteEndObject();
        }
    }
}
