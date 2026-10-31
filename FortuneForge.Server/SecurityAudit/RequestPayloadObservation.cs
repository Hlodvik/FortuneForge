using System.Text.Json;
using System.Text.RegularExpressions;
using FortuneForge.Server.Activity;

namespace FortuneForge.Server.SecurityAudit;

internal sealed record GameRequestPayload(string? Json, string State, string? CommandId, long? StateVersion, string? GameId, string? InstanceId = null);
internal sealed record GameRequestRoute(string GameId, string? InstanceId, bool IsPublicDemo = false);

internal static class GameRequestPayloadObservation
{
    private static readonly Regex Identifier = new("^[a-zA-Z0-9_.:-]{1,128}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
    // Only command-contract fields are retained. Account/profile/payment fields and arbitrary extensions never enter evidence.
    private static readonly HashSet<string> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        "requestId", "operationId", "commandId", "idempotencyKey", "expectedVersion", "expectedRevision", "stateVersion", "version",
        "gameId", "roundId", "matchId", "runId", "tableId", "ticketId", "replayRunId", "competitionId", "entryId",
        "type", "action", "kind", "choice", "decision", "direction", "column", "tile", "quantity", "face", "total", "firstFace", "secondFace",
        "wager", "wagerCents", "wagerPoints", "pointValueInCents", "stake", "ante", "trips", "pairPlus", "primaryStake", "tieStake", "dragonStake", "tigerStake",
        "buyInCents", "entryCredits", "entryFee", "coinsWagered", "handCount", "betSide", "side", "bets", "extraBets", "ticket", "numbers", "number",
        "heldPositions", "mineCount", "drawCount", "seed", "targetScore", "difficulty", "dicePerPlayer", "cards", "card", "suit", "rank",
        "source", "destination", "sourcePile", "destinationPile", "from", "to", "fromPile", "toPile", "fromIndex", "toIndex", "cardIndex", "count", "index",
        "commands", "replay", "step", "input", "totalSteps", "totalTicks", "flapTicks", "jumpTicks", "rightClickTicks", "rulesVersion", "flap",
        "draw", "mode", "seat", "position", "pile", "pileIndex", "tableauIndex", "foundationIndex", "cardCount", "expectedMoves", "expectedScore",
        "zone", "startIndex", "useFreeSpin", "useSpecialBoost", "freeSpinsRemaining", "freeSpinWagerPoints",
        "energyBalance", "sealCollections", "sealId", "averageWagerPoints", "requiredCount", "freeSpinFeatureMode",
        "period", "competition", "entry", "variant", "tableRuleId",
        "raiseTo", "buyInCredits", "stakeCents", "amountCents", "amount", "claim", "elapsedMilliseconds", "elapsedTicks", "commandIndex", "tick", "timestamp"
    };
    private static readonly string[] SensitiveNames = ["password", "token", "cookie", "authorization", "secret", "credential", "email", "phone", "address", "payment"];

    internal static GameRequestRoute? Route(string method, string path)
    {
        if (method is not ("POST" or "PUT" or "PATCH" or "DELETE") || path.Length > 512) return null;
        var parts = path.Trim('/').Split('/');
        if (parts.Length < 3 || !parts[0].Equals("api", StringComparison.OrdinalIgnoreCase)) return null;
        var section = parts[1].ToLowerInvariant();
        var game = section switch
        {
            "solitaire" => "solitaire",
            "slots" when parts[2].Equals("spins", StringComparison.OrdinalIgnoreCase) => "slots",
            "slots" when parts.Length == 4 && parts[2].Equals("demo", StringComparison.OrdinalIgnoreCase) &&
                         parts[3].Equals("spins", StringComparison.OrdinalIgnoreCase) => "slots",
            "cards" or "games" or "arcade" or "arcade-competitions" => parts[2].ToLowerInvariant(),
            _ => null
        };
        if (game is null || game != "slots" && !KnownGames.Contains(game)) return null;
        string? instance = null;
        for (var index = 2; index < parts.Length - 1; index++)
            if (parts[index].ToLowerInvariant() is "rounds" or "matches" or "tables" or "runs" or "games" or "entries" &&
                index + 1 < parts.Length && Identifier.IsMatch(parts[index + 1]) &&
                parts[index + 1].ToLowerInvariant() is not ("queue" or "join" or "start" or "create"))
                instance = parts[index + 1];
        return new(game, instance, section == "slots" && parts.Length == 4 &&
            parts[2].Equals("demo", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly HashSet<string> KnownGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "blackjack", "three-card-poker", "texas-holdem", "solitaire", "hearts", "liars-dice", "2048", "drop-merge", "craps", "roulette",
        "baccarat", "dragon-tiger", "andar-bahar", "keno", "sic-bo", "casino-war", "video-poker", "house-holdem", "hi-lo", "mines",
        "asteroids", "flappy", "horse-flight", "snake"
    };

    internal static GameRequestPayload Parse(ReadOnlyMemory<byte> utf8, string? headerCommandId)
    {
        if (utf8.IsEmpty) return new(null, "empty", SafeId(headerCommandId), null, null);
        try
        {
            using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions { MaxDepth = 12 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new(null, "non-object", SafeId(headerCommandId), null, null);
            if (HasSensitiveProperty(document.RootElement)) return new(null, "sensitive-body-omitted", SafeId(headerCommandId), null, null);
            var root = document.RootElement;
            var command = SafeId(headerCommandId) ?? Text(root, "commandId") ?? Text(root, "requestId") ?? Text(root, "operationId");
            long? version = null;
            foreach (var key in new[] { "expectedVersion", "expectedRevision", "stateVersion", "version" })
                if (root.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetInt64(out var number)) { version = number; break; }
            var gameId = Text(root, "gameId");
            if (gameId is not null && AcceptedGameActivityReader.SlotIds.TryGetValue(gameId, out var catalogue)) gameId = catalogue;
            var instanceId = new[] { "roundId", "matchId", "tableId", "runId", "entryId" }.Select(key => Text(root, key)).FirstOrDefault(id => id is not null);
            var partial = false;
            var nodes = 0;
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) Write(root, writer, ref partial, ref nodes);
            return new(System.Text.Encoding.UTF8.GetString(stream.ToArray()), partial ? "sanitized-partial" : "sanitized", command, version, gameId, instanceId);
        }
        catch (JsonException) { return new(null, "invalid-json", SafeId(headerCommandId), null, null); }
    }

    private static bool HasSensitiveProperty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Any(property => SensitiveNames.Any(name => property.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) || HasSensitiveProperty(property.Value)),
        JsonValueKind.Array => value.EnumerateArray().Any(HasSensitiveProperty),
        _ => false
    };

    private static void Write(JsonElement value, Utf8JsonWriter writer, ref bool partial, ref int nodes)
    {
        if (++nodes > 8192) { writer.WriteNullValue(); partial = true; return; }
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var count = 0;
                foreach (var property in value.EnumerateObject())
                {
                    if (++count > 64 || !Fields.Contains(property.Name)) { partial = true; continue; }
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer, ref partial, ref nodes);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                var items = 0;
                foreach (var item in value.EnumerateArray())
                {
                    if (++items > 1024) { partial = true; break; }
                    Write(item, writer, ref partial, ref nodes);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                var text = value.GetString();
                if (text?.Length > 256) { writer.WriteNullValue(); partial = true; }
                else writer.WriteStringValue(text);
                break;
            default: value.WriteTo(writer); break;
        }
    }

    private static string? Text(JsonElement root, string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? SafeId(value.GetString()) : null;
    internal static string? SafeId(string? value) => value is not null && Identifier.IsMatch(value) ? value : null;
}
