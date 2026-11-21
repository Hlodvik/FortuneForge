using System.Globalization;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.MidnightVault.Server;

internal sealed record MidnightVaultRoundBefore(
    IReadOnlyList<MidnightVaultBonusRound> Rounds, long LegacyRemaining, int SpecialPoints, long Energy, int Pity,
    IReadOnlyDictionary<string, long> SealCounts, IReadOnlyDictionary<string, long> SealWagerPoints)
{
    internal bool Matches(MidnightVaultRoundBefore other) =>
        LegacyRemaining == other.LegacyRemaining && SpecialPoints == other.SpecialPoints && Energy == other.Energy &&
        Pity == other.Pity && Rounds.SequenceEqual(other.Rounds) &&
        Matches(SealCounts, other.SealCounts) && Matches(SealWagerPoints, other.SealWagerPoints);

    private static bool Matches(IReadOnlyDictionary<string, long> left, IReadOnlyDictionary<string, long> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && pair.Value == value);
}

/// <summary>Transfers the deployed Midnight Vault queue and game counters once, preserving each round's terms.</summary>
public static class MidnightVaultRoundStateCodec
{
    public const string OwnerField = "midnightVaultRoundOwner";
    public const string Owner = MidnightVaultEngine.GameId;
    private const string StateField = "midnightVaultRoundState";
    private const long SchemaVersion = 1;
    private const long CentsPerHistoricalPoint = 100;
    private const string LegacySpecialPoints = "specialPoints:" + Owner;
    private const string LegacyEnergy = "energy:" + Owner;
    private static readonly IReadOnlyDictionary<string, object> Empty = new Dictionary<string, object>();

    internal static async Task<MidnightVaultRoundBefore> ReadAsync(Transaction transaction, FirestoreDb database,
        DocumentSnapshot guard, string account, long signedLegacyCount, CancellationToken token)
    {
        var id = SlotSpinGuard.DocumentId(account, Owner);
        if (!guard.Reference.Equals(database.Collection(SlotSpinGuard.Collection).Document(id)))
            throw InvalidState();
        var fields = Fields(guard);
        if (Text(fields, OwnerField) is not null)
            return Decode(fields, id, signedLegacyCount);

        // Only the original game-only counters are imported here. The wallet RPC supplies the scoped legacy spin count.
        var balances = database.Collection("userBalances");
        var snapshots = await transaction.GetAllSnapshotsAsync(
            [balances.Document($"{account}_{LegacySpecialPoints}"), balances.Document($"{account}_{LegacyEnergy}")], token);
        return Decode(fields, id, signedLegacyCount,
            LegacyBalance(snapshots[0], account, LegacySpecialPoints), LegacyBalance(snapshots[1], account, LegacyEnergy));
    }

    internal static MidnightVaultRoundBefore Read(DocumentSnapshot guard, long expectedLegacyCount)
    {
        var fields = Fields(guard);
        if (Text(fields, OwnerField) != Owner) throw InvalidState();
        return Decode(fields, guard.Id, expectedLegacyCount);
    }

    internal static MidnightVaultRoundBefore Decode(IReadOnlyDictionary<string, object> guard, string guardId, long signedLegacyCount,
        long legacySpecialPoints = 0, long legacyEnergy = 0)
    {
        if (signedLegacyCount < 0) throw InvalidState();
        var owner = Text(guard, OwnerField);
        if (owner is null)
        {
            if (guard.ContainsKey(StateField)) throw InvalidState();
            return ReadHistorical(guard, guardId, signedLegacyCount, legacySpecialPoints, legacyEnergy);
        }
        if (owner != Owner || guard.GetValueOrDefault(StateField) is not IReadOnlyDictionary<string, object> data ||
            Counter(data, "version", required: true) != SchemaVersion ||
            Counter(data, "legacyRemaining", required: true) != signedLegacyCount)
            throw InvalidState();

        var rounds = ReadQueue(data, "rounds");
        ValidateLegacyPrefix(rounds, signedLegacyCount);
        return new(rounds, signedLegacyCount, checked((int)Counter(data, "specialPoints", required: true)),
            Counter(data, "energy", required: true), checked((int)Counter(data, "pity", required: true)),
            ReadTracks(data, "sealCounts", required: true), ReadTracks(data, "sealWagerPoints", required: true));
    }

    internal static Dictionary<string, object> Write(MidnightVaultRoundBefore state)
    {
        if (state is { LegacyRemaining: < 0 } or { SpecialPoints: < 0 } or { Energy: < 0 } or { Pity: < 0 })
            throw InvalidState();
        ValidateLegacyPrefix(state.Rounds, state.LegacyRemaining);
        return new()
        {
            [OwnerField] = Owner,
            [StateField] = new Dictionary<string, object>
            {
                ["version"] = SchemaVersion,
                ["rounds"] = state.Rounds.Select(WriteRound).ToArray(),
                ["legacyRemaining"] = state.LegacyRemaining,
                ["specialPoints"] = (long)state.SpecialPoints,
                ["energy"] = state.Energy,
                ["pity"] = (long)state.Pity,
                ["sealCounts"] = WriteTracks(state.SealCounts),
                ["sealWagerPoints"] = WriteTracks(state.SealWagerPoints),
            },
        };
    }

    internal static void UndoImport(Dictionary<string, object> fields)
    {
        fields[OwnerField] = FieldValue.Delete;
        fields[StateField] = FieldValue.Delete;
    }

    private static MidnightVaultRoundBefore ReadHistorical(IReadOnlyDictionary<string, object> guard, string guardId,
        long legacyRemaining, long specialPoints, long energy)
    {
        if (specialPoints < 0 || energy < 0) throw InvalidState();
        IReadOnlyList<MidnightVaultBonusRound> rounds = [];
        if (guard.ContainsKey("midnightBonusRounds"))
            rounds = ReadQueue(guard, "midnightBonusRounds");
        else if (legacyRemaining > 0)
        {
            var cents = Integer(guard, "freeSpinWagerCents");
            if (cents <= 0) cents = checked(Integer(guard, "freeSpinWagerPoints") * CentsPerHistoricalPoint);
            var wager = CentsToPoints(cents);
            var count = checked((int)legacyRemaining);
            rounds = [new($"{guardId}:legacy", count, count, wager, Text(guard, "freeSpinFeatureMode"),
                MidnightVaultBonusPolicy.ResolveModel(Text(guard, "freeSpinMathModelId")))];
        }
        ValidateLegacyPrefix(rounds, legacyRemaining);
        if (rounds.Sum(round => (long)round.RemainingSpins) != legacyRemaining) throw InvalidState();

        var hasWagerCents = guard.ContainsKey("sealWagerCents");
        var trackedWagers = ReadTracks(guard, hasWagerCents ? "sealWagerCents" : "sealWagerTotals");
        var wagerPoints = trackedWagers.ToDictionary(pair => pair.Key, pair => CentsToPoints(
            hasWagerCents ? pair.Value : checked(pair.Value * CentsPerHistoricalPoint)), StringComparer.Ordinal);
        return new(rounds, legacyRemaining, checked((int)specialPoints), energy,
            checked((int)Counter(guard, "consecutiveFiveMisses")), ReadTracks(guard, "sealCounts"), wagerPoints);
    }

    private static void ValidateLegacyPrefix(IReadOnlyList<MidnightVaultBonusRound> rounds, long legacyRemaining)
    {
        MidnightVaultBonusRounds.Validate(rounds);
        foreach (var round in rounds)
        {
            if (legacyRemaining == 0) return;
            legacyRemaining -= round.RemainingSpins;
            if (legacyRemaining < 0) throw InvalidState();
        }
        if (legacyRemaining != 0) throw InvalidState();
    }

    private static IReadOnlyList<MidnightVaultBonusRound> ReadQueue(IReadOnlyDictionary<string, object> fields, string key)
    {
        if (fields.GetValueOrDefault(key) is not IEnumerable<object> raw) throw InvalidState();
        return raw.Select(ReadRound).ToArray();
    }

    private static MidnightVaultBonusRound ReadRound(object raw)
    {
        if (raw is not IReadOnlyDictionary<string, object> data || Text(data, "id") is not { } id ||
            Text(data, "mathModelId") is not { } model)
            throw InvalidState();
        var mode = data.GetValueOrDefault("featureMode") switch
        {
            null or "" => null,
            string value => value,
            _ => throw InvalidState(),
        };
        return new(id, checked((int)Counter(data, "totalSpins", required: true)),
            checked((int)Counter(data, "remainingSpins", required: true)),
            Counter(data, "wagerPoints", required: true), mode, model);
    }

    private static Dictionary<string, object> WriteRound(MidnightVaultBonusRound round) => new()
    {
        ["id"] = round.Id, ["totalSpins"] = (long)round.TotalSpins, ["remainingSpins"] = (long)round.RemainingSpins,
        ["wagerPoints"] = round.WagerPoints, ["featureMode"] = round.FeatureMode ?? string.Empty, ["mathModelId"] = round.MathModelId,
    };

    private static IReadOnlyDictionary<string, object> Fields(DocumentSnapshot snapshot) =>
        snapshot.Exists ? snapshot.ToDictionary() : Empty;

    private static long LegacyBalance(DocumentSnapshot snapshot, string account, string currency)
    {
        var fields = Fields(snapshot);
        if ((Text(fields, "userId") is { } owner && owner != account) ||
            (Text(fields, "currencyId") is { } currencyId && currencyId != currency))
            throw InvalidState();
        return Counter(fields, "available");
    }

    private static long CentsToPoints(long cents)
    {
        const int unit = MidnightVaultSlotDefinitions.WagerPointValueInCents;
        if (cents < 0 || cents % unit != 0) throw InvalidState();
        return cents / unit;
    }

    private static long Counter(IReadOnlyDictionary<string, object> data, string name, bool required = false)
    {
        var value = Integer(data, name, required);
        return value < 0 ? throw InvalidState() : value;
    }

    private static long Integer(IReadOnlyDictionary<string, object> data, string name, bool required = false) =>
        data.GetValueOrDefault(name) switch
        {
            long value => value,
            int value => value,
            null when !required => 0L,
            _ => throw InvalidState(),
        };

    private static string? Text(IReadOnlyDictionary<string, object> data, string name) =>
        data.GetValueOrDefault(name) switch
        {
            null => null,
            string value when string.IsNullOrWhiteSpace(value) => null,
            string value => value,
            _ => throw InvalidState(),
        };

    private static IReadOnlyDictionary<string, long> ReadTracks(IReadOnlyDictionary<string, object> data,
        string key, bool required = false)
    {
        var map = data.GetValueOrDefault(key) as IReadOnlyDictionary<string, object>;
        if (map is null && required) throw InvalidState();
        return MidnightVaultBonusRounds.CollectionTrackIds.ToDictionary(track => track,
            track => map is null ? 0L : required ? Counter(map, track, required: true) :
                LegacyTrackCounter(map.GetValueOrDefault(track)), StringComparer.Ordinal);
    }

    private static long LegacyTrackCounter(object? source)
    {
        var value = source switch
        {
            long integer => integer,
            int integer => integer,
            double number => checked((long)number),
            decimal number => checked((long)number),
            _ => long.TryParse(source?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L,
        };
        return value < 0 ? throw InvalidState() : value;
    }

    private static Dictionary<string, object> WriteTracks(IReadOnlyDictionary<string, long> trackValues) =>
        MidnightVaultBonusRounds.CollectionTrackIds.ToDictionary(track => track, track =>
        {
            var value = trackValues.GetValueOrDefault(track);
            return value < 0 ? throw InvalidState() : (object)value;
        }, StringComparer.Ordinal);

    private static InvalidOperationException InvalidState() => new("The saved Midnight Vault round state is invalid.");
}
