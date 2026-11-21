using System.Text.Json;
using System.Text.Json.Serialization;

namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

internal sealed class ThreeCardPokerTableReplyConverter : JsonConverter<ThreeCardPokerTableSessionResponse>
{
    public ThreeCardPokerTableReplyConverter() { }
    public override ThreeCardPokerTableSessionResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var version = root.GetProperty("version").GetInt32();
        return root.GetProperty("kind").GetString() switch
        {
            ThreeCardPokerTableSessionKinds.Idle => new ThreeCardPokerTableIdleSessionResponse(),
            ThreeCardPokerTableSessionKinds.Table => new ThreeCardPokerTablePlaySessionResponse(
                root.GetProperty("table").Deserialize<ThreeCardPokerTableResponse>(options)!, version),
            ThreeCardPokerTableSessionKinds.Queue => new ThreeCardPokerTableQueueSessionResponse(
                root.GetProperty("ticketId").GetString()!, root.GetProperty("position").GetInt32(),
                root.GetProperty("joinedAtUtc").GetDateTime(), root.GetProperty("humanGraceEndsAtUtc").GetDateTime(),
                root.GetProperty("players").Deserialize<ThreeCardPokerTableSeatResponse[]>(options)!, version),
            _ => throw new JsonException("The recorded Three Card Poker session kind is invalid.")
        };
    }

    public override void Write(Utf8JsonWriter writer, ThreeCardPokerTableSessionResponse value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
}
