using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.HouseHoldem;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.HouseHoldem;

internal sealed record HouseHoldemStoredRound(string RoundId, string UserId, HouseHoldemRound Round,
    string[] Deck, string[] Steps, DateTimeOffset CreatedAt);

/// <summary>The only persistence edge for both legacy deck/action hands and package snapshots.</summary>
internal static class HouseHoldemRoundCodec
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    internal static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    internal static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)
        ?? throw new InvalidOperationException("The House Hold'em document is empty.");

    internal static HouseHoldemStoredRound Read(DocumentSnapshot snapshot) =>
        Read(snapshot.Id, snapshot.ToDictionary());

    internal static void ValidateReplay(HouseHoldemStoredRound stored)
    {
        var replayed = Read(stored.RoundId, Write(stored, stored.CreatedAt, string.Empty));
        if (Serialize(stored) != Serialize(replayed))
            throw new InvalidOperationException("The frozen House Hold'em state does not reproduce its saved hand.");
    }

    internal static HouseHoldemStoredRound Read(string id, IReadOnlyDictionary<string, object> fields)
    {
        if (Text(fields, "roundId") != id || Text(fields, "userId") is not { Length: > 0 } user ||
            Number(fields, "schemaVersion") != 1 || fields.GetValueOrDefault("createdAt") is not Timestamp started)
            throw new InvalidOperationException("The House Hold'em round identity is corrupt.");
        var deck = Strings(fields, "deck");
        var steps = Strings(fields, "steps");
        var ante = Number(fields, "anteCents");
        var trips = Number(fields, "tripsCents");
        // RestoreRound deliberately retains the previous R500 ante ceiling for historical hands.
        var replay = HouseHoldemRules.RestoreRound(deck, ante, trips);
        foreach (var step in steps) Apply(replay, step);
        var round = Text(fields, "privateRoundJson") is { } json
            ? Deserialize<HouseHoldemRound>(json) : replay;
        // Compare rules-owned state, excluding calculated properties and serializer naming differences.
        if (Serialize(round) != Serialize(replay) || Text(fields, "phase") != round.Phase ||
            Number(fields, "totalWagerCents") != round.TotalWagerCents || Number(fields, "payoutCents") != round.PayoutCents)
            throw new InvalidOperationException("The House Hold'em history does not reproduce its saved hand.");
        return new(id, user, round, deck, steps, started.ToDateTimeOffset());
    }

    internal static Dictionary<string, object> Write(HouseHoldemStoredRound stored, DateTimeOffset now, string startKey) => new()
    {
        ["roundId"] = stored.RoundId, ["userId"] = stored.UserId,
        ["anteCents"] = stored.Round.AnteCents, ["tripsCents"] = stored.Round.TripsCents,
        ["deck"] = stored.Deck, ["steps"] = stored.Steps,
        ["privateRoundJson"] = Serialize(stored.Round), ["phase"] = stored.Round.Phase,
        ["totalWagerCents"] = stored.Round.TotalWagerCents, ["payoutCents"] = stored.Round.PayoutCents,
        ["createdAt"] = Timestamp.FromDateTime(stored.CreatedAt.UtcDateTime),
        ["updatedAt"] = Timestamp.FromDateTime(now.UtcDateTime), ["startIdempotencyKey"] = startKey,
        ["schemaVersion"] = 1L, ["backendOwner"] = HouseHoldemBackend.Owner
    };

    internal static void Apply(HouseHoldemRound round, string command)
    {
        if (command == HouseHoldemCommands.Advance) HouseHoldemRules.Advance(round);
        else HouseHoldemRules.ApplyAction(round, command);
    }

    internal static string? Text(DocumentSnapshot snapshot, string key) =>
        snapshot.Exists && snapshot.TryGetValue<string>(key, out var value) ? value : null;
    private static string? Text(IReadOnlyDictionary<string, object> fields, string key) => fields.GetValueOrDefault(key) as string;
    private static long Number(IReadOnlyDictionary<string, object> fields, string key) => fields.GetValueOrDefault(key) is long value
        ? value : throw new InvalidOperationException($"The House Hold'em {key} field is missing.");
    private static string[] Strings(IReadOnlyDictionary<string, object> fields, string key)
    {
        if (fields.GetValueOrDefault(key) is not IEnumerable array)
            throw new InvalidOperationException($"The House Hold'em {key} field is missing.");
        return array.Cast<object>().Select(value => value as string ??
            throw new InvalidOperationException($"The House Hold'em {key} field is invalid.")).ToArray();
    }
}
