using System.Globalization;
using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.JadeDragon.Server;

internal sealed record JadeCarry(int Remaining, long WagerPoints, string? Mode, string EngineVersion);

internal sealed record JadeRoundBefore(int NativeRemaining, int LegacyRemaining, JadeCarry? Carry,
    int SpecialPoints, long Energy, int Pity, IReadOnlyDictionary<string, long> SealCounts,
    IReadOnlyDictionary<string, long> SealWagerPoints)
{
    internal bool Matches(JadeRoundBefore other) =>
        NativeRemaining == other.NativeRemaining && LegacyRemaining == other.LegacyRemaining &&
        Carry == other.Carry && SpecialPoints == other.SpecialPoints && Energy == other.Energy && Pity == other.Pity &&
        Matches(SealCounts, other.SealCounts) && Matches(SealWagerPoints, other.SealWagerPoints);

    private static bool Matches(IReadOnlyDictionary<string, long> left, IReadOnlyDictionary<string, long> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);
}

/// <summary>Imports deployed Jade-only game counters once and then reads the owned game state.</summary>
public static class JadeRoundStateCodec
{
    public const string OwnerField = "jadeRoundOwner";
    public const string Owner = "jade-dragon-v1";
    private const string StateField = "jadeRoundState";
    private const long StateVersion = 1;
    private const long CentsPerHistoricalPoint = 100;
    private const string LegacySpecialPoints = "specialPoints";
    private const string LegacyEnergy = "energy";
    private static readonly IReadOnlyDictionary<string, object> Empty = new Dictionary<string, object>();

    internal static async Task<JadeRoundBefore> ReadAsync(Transaction transaction, FirestoreDb database,
        DocumentSnapshot guard, string account, long signedLegacyCount, CancellationToken token)
    {
        var guardId = SlotSpinGuard.DocumentId(account, JadeDragonEngine.GameId);
        if (!guard.Reference.Equals(database.Collection(SlotSpinGuard.Collection).Document(guardId)))
            throw CorruptState();

        var fields = Fields(guard);
        if (Text(fields, OwnerField) is not null)
            return Decode(fields, signedLegacyCount);

        // Only the two pre-cutover noncash counters belong in this import. The wallet RPC supplies legacy free units.
        var pointsId = LegacyCurrency(LegacySpecialPoints);
        var energyId = LegacyCurrency(LegacyEnergy);
        var historical = database.Collection("userBalances");
        var snapshots = await transaction.GetAllSnapshotsAsync(
            [historical.Document($"{account}_{pointsId}"), historical.Document($"{account}_{energyId}")], token);
        return Decode(fields, signedLegacyCount, ReadLegacyBalance(snapshots[0], account, pointsId),
            ReadLegacyBalance(snapshots[1], account, energyId));
    }

    internal static JadeRoundBefore Read(DocumentSnapshot guard, long signedLegacyCount) =>
        Decode(Fields(guard), signedLegacyCount);

    internal static JadeRoundBefore Decode(IReadOnlyDictionary<string, object> guard, long signedLegacyCount,
        long legacySpecialPoints = 0, long legacyEnergy = 0)
    {
        var legacyRemaining = checked((int)Counter(signedLegacyCount));
        var owner = Text(guard, OwnerField);
        if (owner is null)
            return ReadHistorical(guard, legacyRemaining, legacySpecialPoints, legacyEnergy);
        if (owner != Owner || guard.GetValueOrDefault(StateField) is not IReadOnlyDictionary<string, object> progress ||
            Number(progress, "version", required: true) != StateVersion)
            throw CorruptState();

        var nativeRemaining = checked((int)Counter(Number(progress, "nativeRemaining", required: true)));
        var carry = ReadCarry(checked(nativeRemaining + legacyRemaining), Number(progress, "wagerPoints", required: true),
            Text(progress, "mode"), Text(progress, "engineVersion"));
        return new(nativeRemaining, legacyRemaining, carry,
            checked((int)Counter(Number(progress, "specialPoints", required: true))),
            Counter(Number(progress, "energy", required: true)), checked((int)Counter(Number(progress, "pity", required: true))),
            ReadTrackMap(progress, "sealCounts", required: true), ReadTrackMap(progress, "sealWagerPoints", required: true));
    }

    internal static Dictionary<string, object> Write(JadeRoundBefore state)
    {
        var remaining = checked((int)Counter(state.NativeRemaining) + (int)Counter(state.LegacyRemaining));
        var carry = ReadCarry(remaining, state.Carry?.WagerPoints ?? 0, state.Carry?.Mode, state.Carry?.EngineVersion);
        if (carry != state.Carry) throw CorruptState();

        return new()
        {
            [OwnerField] = Owner,
            [StateField] = new Dictionary<string, object>
            {
                ["version"] = StateVersion,
                ["nativeRemaining"] = (long)state.NativeRemaining,
                ["wagerPoints"] = carry?.WagerPoints ?? 0L,
                ["mode"] = carry?.Mode ?? string.Empty,
                ["engineVersion"] = carry?.EngineVersion ?? string.Empty,
                ["specialPoints"] = Counter(state.SpecialPoints),
                ["energy"] = Counter(state.Energy),
                ["pity"] = Counter(state.Pity),
                ["sealCounts"] = WriteTrackMap(state.SealCounts),
                ["sealWagerPoints"] = WriteTrackMap(state.SealWagerPoints),
            }
        };
    }

    internal static void UndoImport(Dictionary<string, object> fields)
    {
        fields[OwnerField] = FieldValue.Delete;
        fields[StateField] = FieldValue.Delete;
    }

    private static JadeRoundBefore ReadHistorical(IReadOnlyDictionary<string, object> guard, int remaining,
        long specialPoints, long energy)
    {
        JadeCarry? carry = null;
        if (remaining > 0)
        {
            var wagerCents = Number(guard, "freeSpinWagerCents");
            if (wagerCents <= 0)
                wagerCents = checked(Number(guard, "freeSpinWagerPoints") * CentsPerHistoricalPoint);
            var model = Text(guard, "freeSpinMathModelId");
            if (model is not null && model != JadeDragonCascadeRules.Version)
                throw CorruptState();
            carry = ReadCarry(remaining, CentsToPoints(wagerCents), Text(guard, "freeSpinFeatureMode"),
                model ?? JadeDragonEngine.Version);
        }

        var sealCents = guard.ContainsKey("sealWagerCents");
        var seals = ReadTrackMap(guard, sealCents ? "sealWagerCents" : "sealWagerTotals");
        var sealWagerPoints = seals.ToDictionary(pair => pair.Key, pair =>
            CentsToPoints(sealCents ? pair.Value : checked(pair.Value * CentsPerHistoricalPoint)), StringComparer.Ordinal);
        return new(0, remaining, carry, checked((int)Counter(specialPoints)), Counter(energy),
            checked((int)Counter(Number(guard, "consecutiveFiveMisses"))), ReadTrackMap(guard, "sealCounts"), sealWagerPoints);
    }

    private static JadeCarry? ReadCarry(int remaining, long wagerPoints, string? mode, string? engine)
    {
        if (remaining == 0)
            return null;
        if (engine == JadeDragonCascadeRules.Version)
        {
            if (mode is not null) throw CorruptState();
            JadeDragonCascadeRules.ValidatePaidWager(wagerPoints);
        }
        else if (engine != JadeDragonEngine.Version || wagerPoints <= 0 ||
            mode is not (null or JadeDragonCollections.CollectionFeatureMode))
        {
            throw CorruptState();
        }
        return new(remaining, wagerPoints, mode, engine);
    }

    private static long CentsToPoints(long cents)
    {
        var unit = JadeDragonCascadeRules.WagerPointValueInCents;
        if (cents < 0 || cents % unit != 0) throw CorruptState();
        return cents / unit;
    }

    private static string LegacyCurrency(string prefix) => $"{prefix}:{JadeDragonEngine.GameId}";

    private static IReadOnlyDictionary<string, object> Fields(DocumentSnapshot document) =>
        document.Exists ? document.ToDictionary() : Empty;

    private static long ReadLegacyBalance(DocumentSnapshot document, string account, string currency)
    {
        var data = Fields(document);
        if ((Text(data, "userId") is { } userId && userId != account) ||
            (Text(data, "currencyId") is { } currencyId && currencyId != currency))
            throw CorruptState();
        return Number(data, "available");
    }

    private static long Counter(long value) => value < 0 ? throw CorruptState() : value;

    private static long Number(IReadOnlyDictionary<string, object> data, string key, bool required = false) =>
        data.GetValueOrDefault(key) switch
        {
            long value => value,
            int value => value,
            null when !required => 0L,
            _ => throw CorruptState(),
        };

    private static string? Text(IReadOnlyDictionary<string, object> data, string key) =>
        data.GetValueOrDefault(key) switch
        {
            null => null,
            string value when string.IsNullOrWhiteSpace(value) => null,
            string value => value,
            _ => throw CorruptState(),
        };

    private static IReadOnlyDictionary<string, long> ReadTrackMap(IReadOnlyDictionary<string, object> data,
        string key, bool required = false)
    {
        var map = data.GetValueOrDefault(key) as IReadOnlyDictionary<string, object>;
        if (map is null && required) throw CorruptState();
        return JadeDragonCollections.TrackIds.ToDictionary(track => track,
            track => map is null ? 0L : Counter(required ? Number(map, track, required: true) :
                HistoricalSealNumber(map.GetValueOrDefault(track))), StringComparer.Ordinal);
    }

    private static long HistoricalSealNumber(object? value) => value switch
    {
        long integer => integer,
        int integer => integer,
        double number => checked((long)number),
        decimal number => checked((long)number),
        _ => long.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L,
    };

    private static Dictionary<string, object> WriteTrackMap(IReadOnlyDictionary<string, long> values) =>
        JadeDragonCollections.TrackIds.ToDictionary(track => track, track => (object)Counter(values.GetValueOrDefault(track)),
            StringComparer.Ordinal);

    private static InvalidOperationException CorruptState() => new("The saved Jade Dragon feature state is invalid.");
}
