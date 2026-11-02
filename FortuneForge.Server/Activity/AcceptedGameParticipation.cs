using System.Text.Json;
using System.Text.RegularExpressions;

namespace FortuneForge.Server.Activity;

// Experience can include a committed stake/dealt match before its first move.
// These witnesses do not change popularity or Home's accepted-action recency.
internal static class AcceptedGameParticipation
{
    private static readonly (string Game, Regex Path)[] Additional =
    [
        Rule("texas-holdem", @"/api/cards/texas-holdem/credit/(queue|matches/[^/]+/next-hand)"),
        Rule("hearts", @"/api/games/hearts/((paid/(join|matches/[^/]+/play-house))|matches|trial)"),
        Rule("liars-dice", @"/api/games/liars-dice/matches"),
        Rule("craps", @"/api/games/craps/rounds(/[^/]+/(odds|wagers))?"),
        Rule("snake", @"/api/games/snake/games/[^/]+/(turn|tick)"),
        .. new[] { "asteroids", "flappy", "horse-flight" }.SelectMany(game => new[]
        {
            Rule(game, $@"/api/arcade-competitions/{game}/(free|daily|weekly)/runs/[^/]+/replay"),
            Rule(game, $@"/api/arcade-competitions/{game}/(daily|weekly)/attempts"),
        }),
        Rule("horse-flight", @"/api/games/horse-flight/runs/[^/]+/complete"),
    ];

    private static (string, Regex) Rule(string game, string path) =>
        (game, new($"^{path}/?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)));

    internal static string? GameFor(string method, string path) => method == "POST"
        ? AcceptedGameActivityReader.GameFor(method, path) ?? Additional.FirstOrDefault(rule => rule.Path.IsMatch(path)).Game
        : null;

    internal static AcceptedGameActivity? Read(string game, string path, string json, string? key)
    {
        if (AcceptedGameActivityReader.GameFor("POST", path) is { } recentGame)
            return AcceptedGameActivityReader.Read(recentGame, json, key);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Find(root, "error") is not null || Find(root, "code") is not null) return null;
        string? id;
        if (game == "texas-holdem")
        {
            if (Text(root, "kind") is not ("match" or "result") || Number(root, "handNumber") is not > 0) return null;
            id = Text(root, "matchId");
        }
        else if (game == "hearts")
        {
            if (Text(root, "kind") is "queue" or "idle" || Text(root, "phase") is not ("passing" or "playing" or "complete")) return null;
            id = Text(root, "matchId");
        }
        else if (game == "liars-dice")
        {
            if (Text(root, "phase") is not ("bidding" or "resolved")) return null;
            id = Text(root, "matchId");
        }
        else if (game == "craps")
        {
            if (Number(root, "totalWagerCents") is not > 0 || Number(root, "stake") is not > 0) return null;
            id = Text(root, "roundId");
        }
        else if (game == "snake")
        {
            if (Number(root, "moves") is not > 0 || Text(root, "lastEvent") is "no-op" or "started") return null;
            id = Text(root, "gameId");
        }
        else if (game is "asteroids" or "flappy" or "horse-flight")
        {
            if (path.TrimEnd('/').EndsWith("/attempts", StringComparison.OrdinalIgnoreCase))
            {
                if (Text(root, "attemptId") is null || Number(root, "entryFeeCents") is not > 0) return null;
            }
            else if (Number(root, "score") is not >= 0 ||
                     (Text(root, "terminal") ?? Text(root, "phase")) is not { Length: > 0 } phase || phase is "running" or "playing") return null;
            id = Text(root, "runId");
        }
        else return null;
        return string.IsNullOrWhiteSpace(id) || id.Length > 256 ? null
            : new(game, id, AcceptedGameActivityReader.Hash(string.IsNullOrWhiteSpace(key) ? json : key));
    }

    private static double? Number(JsonElement root, string name)
    {
        var value = Find(root, name);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.Number
            && value.Value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
    }
    private static string? Text(JsonElement root, string name)
    {
        var value = Find(root, name);
        return value.HasValue && value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString() : null;
    }
    private static JsonElement? Find(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)) return value;
        foreach (var envelope in new[] { "session", "table", "round", "match", "state", "result" })
            if (root.TryGetProperty(envelope, out var child) && child.ValueKind == JsonValueKind.Object && Find(child, name) is { } found) return found;
        return null;
    }
}
