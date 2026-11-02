using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FortuneForge.Server.Activity;

internal sealed record AcceptedGameActivity(string GameId, string RoundKey, string EventKey, bool IsTerminal = false);

// This allowlist is deliberately about accepted play, never navigation, queueing,
// balance reads, result acknowledgements, or automated polling.
internal static class AcceptedGameActivityReader
{
    internal static readonly IReadOnlyDictionary<string, string> SlotIds = new Dictionary<string, string>
    {
        ["wukong-journey-v1"] = "wukong-journey-to-the-west",
        ["rainbow-realm-fruits-v1"] = "rainbow-realm",
        ["pirates-fortune-v1"] = "pirates-fortune", ["gods-of-olympus-v1"] = "gods-of-olympus",
        ["reel-riches-v1"] = "reel-riches", ["high-noon-fortune-v1"] = "high-noon-fortune",
        ["royal-draw-v1"] = "royal-draw", ["arcane-archives-v1"] = "arcane-archives",
        ["cosmic-fortune-v1"] = "cosmic-fortune", ["dino-dominion-v1"] = "dino-dominion",
        ["neon-nights-v1"] = "neon-nights", ["jungle-jackpot-v1"] = "jungle-jackpot",
        ["ocean-odyssey-v1"] = "ocean-odyssey", ["samurai-fortune-v1"] = "samurai-fortune",
        ["candy-carnival-v1"] = "candy-carnival", ["phantom-manor-v1"] = "phantom-manor",
        ["nordic-legends-v1"] = "nordic-legends", ["desert-treasures-v1"] = "desert-treasures",
        ["robot-revolution-v1"] = "robot-revolution", ["dragon-hoard-v1"] = "dragon-hoard",
    };

    private static readonly (string Game, Regex Path)[] Actions =
    [
        Rule("slots", @"/api/slots/spins"),
        Rule("blackjack", @"/api/cards/blackjack/table/tables/[^/]+/(wagers|actions)"),
        Rule("blackjack", @"/api/cards/blackjack/games(/[^/]+/actions)?"),
        Rule("three-card-poker", @"/api/cards/three-card-poker/table/tables/[^/]+/(wagers|actions)"),
        Rule("texas-holdem", @"/api/cards/texas-holdem/credit/matches/[^/]+/actions"),
        Rule("solitaire", @"/api/solitaire/(matches/[^/]+/commands|free/runs/[^/]+/replay)"),
        Rule("hearts", @"/api/games/hearts/(paid/)?matches/[^/]+/(pass|card)"),
        Rule("liars-dice", @"/api/games/liars-dice/matches/[^/]+/(bid|challenge|spot-on)"),
        Rule("2048", @"/api/games/2048/games/[^/]+/move"),
        Rule("drop-merge", @"/api/games/drop-merge/games/[^/]+/drop"),
        Rule("craps", @"/api/games/craps/rounds/[^/]+/roll"),
        Rule("roulette", @"/api/games/roulette/rounds/[^/]+/spin"),
        .. new[] { "baccarat", "dragon-tiger", "andar-bahar", "keno", "sic-bo", "casino-war", "video-poker", "house-holdem", "hi-lo", "mines" }
            .Select(game => Rule(game, $@"/api/games/{game}/rounds(/[^/]+/(actions|draw|decision|reveal|cashout))?")),
    ];

    private static (string, Regex) Rule(string game, string path) =>
        (game, new Regex($"^{path}/?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)));

    internal static string? GameFor(string method, string path) => method == "POST"
        ? Actions.FirstOrDefault(rule => rule.Path.IsMatch(path)).Game : null;

    internal static AcceptedGameActivity? Read(string game, string responseJson, string? requestKey)
    {
        using var document = JsonDocument.Parse(responseJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Find(root, "error") is not null) return null;
        if (game == "slots")
        {
            var serverGame = Text(root, "gameId");
            if (serverGame is null || !SlotIds.TryGetValue(serverGame, out game!)) return null;
            var spinId = Text(root, "spinId");
            return spinId is null ? null : new(game, spinId, spinId, true);
        }

        // Free Solitaire shares the legacy replay endpoint. Only a server-owned
        // free-run receipt can identify the separate catalogue game.
        if (game == "solitaire" && Text(root, "runId")?.StartsWith("solitaire_free_", StringComparison.Ordinal) == true
            && Text(root, "gameId") == "free-solitaire")
            game = "free-solitaire";

        var id = game switch
        {
            "blackjack" => Join(Text(root, "tableId"), Text(root, "round")) ?? (Text(root, "gameId") is { } legacyId ? "legacy:" + legacyId : null),
            "three-card-poker" => Join(Text(root, "tableId"), Text(root, "round")),
            "texas-holdem" => Join(Text(root, "matchId"), Text(root, "handNumber")),
            "solitaire" or "free-solitaire" => Text(root, "runId") ?? Text(root, "matchId"),
            "2048" or "drop-merge" => Text(root, "runId"),
            "roulette" => Join(Text(root, "tableId"), Text(root, "roundNumber")),
            "hearts" or "liars-dice" => Text(root, "matchId"),
            _ => Text(root, "roundId"),
        };
        if (id is null || id.Length > 256) return null;
        if (game is "2048" or "drop-merge" or "solitaire" or "free-solitaire" && Text(root, "moves") == "0") return null;
        if (game is "2048" or "drop-merge" && Text(root, "lastEvent") == "no-move") return null;
        // Persist only identifiers and timestamps, never cards, outcomes, balances,
        // request bodies, or account tokens. Stable receipts make retries harmless.
        var eventKey = !string.IsNullOrWhiteSpace(requestKey)
            ? Hash(requestKey) : Hash(responseJson);
        var terminal = new[] { Text(root, "status"), Text(root, "phase"), Text(root, "kind") }.Any(value =>
            value?.Replace("-", "").ToLowerInvariant() is "settled" or "complete" or "completed" or "result" or "results" or "resolved" or "won" or "lost" or "cashedout" or "maximumreturn" or "cleared" or "consolation" or "finished");
        return new(game, id, eventKey, terminal);
    }

    private static string? Join(string? id, string? number) => id is null || number is null ? null : $"{id}:{number}";
    internal static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    private static string? Text(JsonElement root, string name)
    {
        var value = Find(root, name);
        return value?.ValueKind is JsonValueKind.String or JsonValueKind.Number ? value.Value.ToString() : null;
    }

    // Only visit the known envelope fields. Never mistake a player's historical
    // hand/result inside an array for the currently accepted game.
    private static JsonElement? Find(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty(name, out var value) && value.ValueKind is not JsonValueKind.Object and not JsonValueKind.Null) return value;
        foreach (var envelope in new[] { "session", "table", "round", "match", "state", "result" })
            if (root.TryGetProperty(envelope, out var child) && child.ValueKind == JsonValueKind.Object && Find(child, name) is { } found)
                return found;
        return null;
    }
}
