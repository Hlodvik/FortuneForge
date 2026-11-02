using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Activity;

// These observations are constructed from authoritative state and are already written in
// its transaction. Request metadata alone, queue admission and issued free seeds do not qualify.
internal static class AcceptedGameTransactionWitness
{
    internal static IReadOnlyList<string> Players(GameSecurityEventDocument observation)
    {
        if (!observation.Fields.TryGetValue("gameId", out var rawGame) || rawGame is not string game ||
            game is not ("blackjack" or "three-card-poker" or "texas-holdem" or "hearts" or
                "solitaire" or "free-solitaire" or "snake" or "asteroids" or "flappy" or "horse-flight" or
                "baccarat" or "keno" or "sic-bo" or "casino-war" or "video-poker" or "house-holdem" or "craps") ||
            !observation.Fields.TryGetValue("eventKind", out var rawKind) || rawKind is not string kind ||
            !observation.Fields.TryGetValue("payloadJson", out var rawPayload) || rawPayload is not string payload) return [];
        var actors = observation.Fields.TryGetValue("participantIds", out var participants) && participants is IEnumerable<string> ids
            ? ids.Where(ValidPlayer).Distinct(StringComparer.Ordinal).ToArray() : [];
        if (actors.Length == 0) return [];
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var requestPath = Text(Child(root, "request"), "path");
            if (requestPath?.Split('/').Any(segment => string.Equals(segment, "demo", StringComparison.OrdinalIgnoreCase)) == true) return [];
            var state = Child(root, "state");
            if (state.ValueKind != JsonValueKind.Object) return [];
            switch (game)
            {
                case "blackjack":
                case "three-card-poker":
                    if (Child(state, "previous").ValueKind == JsonValueKind.Object) state = Child(state, "previous");
                    if (Child(state, "players").ValueKind == JsonValueKind.Array)
                        return SelectPlayers(state, actors, "actorId", player =>
                            Positive(player, "totalWagerCents") && Count(player, "cards") >= 2);
                    if (game != "blackjack") return [];
                    if (Child(state, "game").ValueKind == JsonValueKind.Object) state = Child(state, "game");
                    return Positive(state, "totalWagerCents") && Count(state, "playerCards") >= 2
                        ? Owner(state, actors) : [];
                case "texas-holdem":
                    return Positive(state, "handNumber") ? SelectPlayers(state, actors, "actorId", player =>
                        True(player, "isAccountBacked") && Count(player, "holeCards") == 2) : [];
                case "hearts":
                    var hearts = Child(state, "state");
                    return Text(state, "status") is "match" or "result" && Positive(state, "entryCents") &&
                        Positive(hearts, "roundNumber") && Child(hearts, "round").ValueKind == JsonValueKind.Object
                        ? SelectPlayers(state, actors, "userId", player => True(player, "isAccountBacked")) : [];
                case "solitaire":
                case "free-solitaire":
                    if (Child(state, "players").ValueKind == JsonValueKind.Array)
                        return SelectPlayers(state, actors, "userId", player =>
                            True(player, "isAccountBacked") && (Positive(Child(player, "game"), "moves") ||
                                game == "solitaire" && PaidDealtSolitaireSeat(Child(state, "match"), player)));
                    return kind == "free-replay-complete" && Positive(Child(state, "game"), "moves") ? actors : [];
                case "snake":
                    return kind is "turned" or "moved" or "ate-food" or "won" or "lost" &&
                        Positive(Child(state, "state"), "moves") ? Owner(state, actors) : [];
                case "asteroids":
                case "flappy":
                case "horse-flight":
                    if (kind == "run-started")
                        return Positive(state, "entryFeeCents") && Text(state, "attemptId") is { Length: > 0 } ? actors : [];
                    var terminal = Text(state, "terminal") ?? Text(state, "phase");
                    return kind == "run-completed" && Number(state, "score") is >= 0 &&
                        terminal is { Length: > 0 } && terminal is not ("running" or "playing") ? actors : [];
                case "baccarat":
                    return kind == "round-settled" && Positive(state, "totalStake") ? Owner(state, actors) : [];
                case "keno":
                    return kind == "round-settled" && Positive(state, "wagerCents") ? Owner(state, actors) : [];
                case "sic-bo":
                    return kind == "round-settled" && Elements(state, "bets").Any(bet => Positive(bet, "stakeCents"))
                        ? Owner(state, actors) : [];
                case "casino-war":
                    return kind is "opening" or "round-settled" && Positive(Child(state, "round"), "primaryStake")
                        ? Owner(state, actors) : [];
                case "video-poker":
                    return kind is "deal" or "round-settled" && Positive(state, "coinsWagered") && Positive(state, "handCount") &&
                        Count(Child(state, "initialDeal"), "cards") == 5 ? actors : [];
                case "house-holdem":
                    var hand = Child(state, "round");
                    return Positive(hand, "anteCents") && Count(hand, "playerCards") == 2 ? actors : [];
                case "craps":
                    var bet = Child(Child(state, "state"), "bet");
                    return Positive(state, "totalWagerCents") && Positive(bet, "stake")
                        ? actors.Where(actor => actor == Text(bet, "playerId")).ToArray() : [];
                default: return [];
            }
        }
        catch (JsonException) { return []; }
        catch (InvalidOperationException) { return []; }
    }

    internal static void Write(FirestoreDb database, GameSecurityEventDocument observation,
        Action<DocumentReference, Dictionary<string, object>> set)
    {
        if (!observation.Fields.TryGetValue("occurredAtUtc", out var rawTime) || rawTime is not DateTime at || at.Kind != DateTimeKind.Utc) return;
        foreach (var userId in Players(observation))
        {
            var witness = AcceptedGameParticipationDocuments.Create(userId, (string)observation.Fields["gameId"], at);
            set(database.Collection("catalogPlayerActivity").Document(witness.DocOwnerKey)
                .Collection("games").Document(witness.GameId), witness.Fields);
        }
    }

    private static IReadOnlyList<string> SelectPlayers(JsonElement state, string[] actors, string idField,
        Func<JsonElement, bool> eligible) => Elements(state, "players").Where(eligible)
        .Select(player => Text(player, idField)).Where(id => id is not null && actors.Contains(id, StringComparer.Ordinal))
        .Select(id => id!).Distinct(StringComparer.Ordinal).ToArray();
    private static IReadOnlyList<string> Owner(JsonElement state, string[] actors) =>
        actors.Where(actor => actor == Text(state, "userId")).ToArray();
    private static bool PaidDealtSolitaireSeat(JsonElement match, JsonElement player)
    {
        if (Text(match, "status") != "playing" || Number(match, "buyInCents") is not { } buyIn || buyIn <= 0 ||
            Number(match, "humanEntryCents") is not { } funded || funded < buyIn ||
            Text(match, "matchId") is not { Length: > 0 } matchId || Text(player, "matchId") != matchId ||
            Text(player, "status") != "playing" || Text(player, "userId") is not { } userId) return false;
        var players = Elements(match, "playerIds").ToArray();
        var tickets = Elements(match, "ticketIds").ToArray();
        var seat = Array.FindIndex(players, id => id.ValueKind == JsonValueKind.String && id.GetString() == userId);
        if (seat < 0 || seat >= tickets.Length || tickets[seat].ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(tickets[seat].GetString())) return false;
        var board = Child(player, "game");
        if (Number(board, "moves") != 0 || Count(board, "tableau") != 7 || Count(board, "foundations") != 4) return false;
        var cards = Elements(board, "stock").Concat(Elements(board, "waste"))
            .Concat(Elements(board, "foundations").Concat(Elements(board, "tableau"))
                .SelectMany(pile => pile.ValueKind == JsonValueKind.Array ? pile.EnumerateArray().ToArray() : []))
            .Select(card => Text(card, "id")).ToArray();
        return cards.Length == 52 && cards.All(id => !string.IsNullOrWhiteSpace(id)) &&
            cards.OfType<string>().Distinct(StringComparer.Ordinal).Count() == 52;
    }
    private static bool ValidPlayer(string id) => !string.IsNullOrWhiteSpace(id) && id.Length <= 512 && !id.Any(char.IsControl);
    private static JsonElement Child(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var child) ? child : default;
    private static string? Text(JsonElement value, string name) => Child(value, name) is var child &&
        child.ValueKind == JsonValueKind.String ? child.GetString() : null;
    private static decimal? Number(JsonElement value, string name) => Child(value, name) is var child &&
        child.ValueKind == JsonValueKind.Number && child.TryGetDecimal(out var number) ? number : null;
    private static bool Positive(JsonElement value, string name) => Number(value, name) is > 0;
    private static bool True(JsonElement value, string name) => Child(value, name).ValueKind == JsonValueKind.True;
    private static int Count(JsonElement value, string name) => Child(value, name) is var child &&
        child.ValueKind == JsonValueKind.Array ? child.GetArrayLength() : 0;
    private static IEnumerable<JsonElement> Elements(JsonElement value, string name) => Child(value, name) is var child &&
        child.ValueKind == JsonValueKind.Array ? child.EnumerateArray().ToArray() : [];
}
