using System.Globalization;
using System.Text.Json.Serialization;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Slots.Persistence;

public sealed record ClassicSlotRoundCarry(int Remaining, long? WagerPoints, string? Mode);

public sealed record ClassicSlotRoundState(int NativeRemaining, int LegacyRemaining, ClassicSlotRoundCarry? Carry,
    int SpecialPoints, long Energy, int Pity, IReadOnlyDictionary<string, long> SealCounts,
    IReadOnlyDictionary<string, long> SealWagerPoints)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NativeStateJson { get; init; }

    public bool Matches(ClassicSlotRoundState other) =>
        NativeRemaining == other.NativeRemaining && LegacyRemaining == other.LegacyRemaining && Carry == other.Carry &&
        SpecialPoints == other.SpecialPoints && Energy == other.Energy && Pity == other.Pity && NativeStateJson == other.NativeStateJson &&
        Matches(SealCounts, other.SealCounts) && Matches(SealWagerPoints, other.SealWagerPoints);

    private static bool Matches(IReadOnlyDictionary<string, long> left, IReadOnlyDictionary<string, long> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);
}

/// <summary>Persists a classic slot's single merged carry and imports only the game's scoped historical counters.</summary>
public sealed class ClassicSlotRoundStateCodec(string storageGameId, string ownerField, string owner, string stateField,
    string gameName, bool usesEnergy, decimal pointValueInCents, Action<long> validateCarryWager,
    Action<string>? validateNativeState = null)
{
    public ClassicSlotRoundStateCodec(string storageGameId, string ownerField, string owner, string stateField,
        string gameName, bool usesEnergy, decimal pointValueInCents, Action<long> validateCarryWager)
        : this(storageGameId, ownerField, owner, stateField, gameName, usesEnergy, pointValueInCents, validateCarryWager, null) { }

    private const long SchemaVersion = 1;
    private const long CentsPerHistoricalPoint = 100;
    private readonly string legacySpecialPointsCurrency = $"specialPoints:{storageGameId}";
    private readonly string legacyEnergyCurrency = $"energy:{storageGameId}";
    private static readonly IReadOnlyDictionary<string, object> Empty = new Dictionary<string, object>();

    public async Task<ClassicSlotRoundState> ReadAsync(Transaction transaction, FirestoreDb database,
        DocumentSnapshot guard, string account, long signedLegacyCount, CancellationToken token)
    {
        var id = SlotSpinGuard.DocumentId(account, storageGameId);
        if (!guard.Reference.Equals(database.Collection(SlotSpinGuard.Collection).Document(id)))
            throw InvalidState();
        var fields = Fields(guard);
        if (Text(fields, ownerField) is not null)
            return Decode(fields, signedLegacyCount);

        // Read only the old game-unit counters. The wallet reports and consumes legacy free units.
        var balances = database.Collection("userBalances");
        List<DocumentReference> reads = [balances.Document($"{account}_{legacySpecialPointsCurrency}")];
        if (usesEnergy) reads.Add(balances.Document($"{account}_{legacyEnergyCurrency}"));
        var snapshots = await transaction.GetAllSnapshotsAsync(reads, token);
        var energy = usesEnergy ? LegacyBalance(snapshots[1], account, legacyEnergyCurrency) : 0;
        return Decode(fields, signedLegacyCount, LegacyBalance(snapshots[0], account, legacySpecialPointsCurrency), energy);
    }

    public ClassicSlotRoundState Read(DocumentSnapshot guard, long expectedLegacyCount)
    {
        var fields = Fields(guard);
        if (Text(fields, ownerField) != owner) throw InvalidState();
        return Decode(fields, expectedLegacyCount);
    }

    public ClassicSlotRoundState Decode(IReadOnlyDictionary<string, object> guard, long signedLegacyCount,
        long legacySpecialPoints = 0, long legacyEnergy = 0)
    {
        if (signedLegacyCount < 0) throw InvalidState();
        var legacyRemaining = checked((int)signedLegacyCount);
        var storedOwner = Text(guard, ownerField);
        if (storedOwner is null)
        {
            if (guard.ContainsKey(stateField)) throw InvalidState();
            return ReadHistorical(guard, legacyRemaining, legacySpecialPoints, usesEnergy ? legacyEnergy : 0);
        }
        if (storedOwner != owner || guard.GetValueOrDefault(stateField) is not IReadOnlyDictionary<string, object> state ||
            Counter(state, "version", required: true) != SchemaVersion ||
            Counter(state, "legacyRemaining", required: true) != legacyRemaining)
            throw InvalidState();

        var nativeRemaining = checked((int)Counter(state, "nativeRemaining", required: true));
        var lockedWager = Counter(state, "wagerPoints", required: true);
        var carry = ReadCarry(nativeRemaining, legacyRemaining, lockedWager > 0 ? lockedWager : null, Text(state, "mode"));
        return new(nativeRemaining, legacyRemaining, carry, checked((int)Counter(state, "specialPoints", required: true)),
            usesEnergy ? Counter(state, "energy", required: true) : 0, checked((int)Counter(state, "pity", required: true)),
            ReadTracks(state, "sealCounts", required: true), ReadTracks(state, "sealWagerPoints", required: true))
        { NativeStateJson = ReadNativeState(state) };
    }

    public Dictionary<string, object> Write(ClassicSlotRoundState state)
    {
        if (state is { NativeRemaining: < 0 } or { LegacyRemaining: < 0 } or { SpecialPoints: < 0 } or { Energy: < 0 } or { Pity: < 0 } ||
            !usesEnergy && state.Energy != 0)
            throw InvalidState();
        var carry = ReadCarry(state.NativeRemaining, state.LegacyRemaining, state.Carry?.WagerPoints, state.Carry?.Mode);
        if (carry != state.Carry) throw InvalidState();
        var stored = new Dictionary<string, object>
        {
            ["version"] = SchemaVersion,
            ["nativeRemaining"] = (long)state.NativeRemaining,
            ["legacyRemaining"] = (long)state.LegacyRemaining,
            ["wagerPoints"] = carry?.WagerPoints ?? 0L,
            ["mode"] = carry?.Mode ?? string.Empty,
            ["specialPoints"] = (long)state.SpecialPoints,
            ["pity"] = (long)state.Pity,
            ["sealCounts"] = WriteTracks(state.SealCounts),
            ["sealWagerPoints"] = WriteTracks(state.SealWagerPoints),
        };
        if (usesEnergy) stored["energy"] = state.Energy;
        if (state.NativeStateJson is { } native)
        {
            if (validateNativeState is null) throw InvalidState();
            validateNativeState(native);
            stored["nativeStateJson"] = native;
        }
        return new() { [ownerField] = owner, [stateField] = stored };
    }

    public void UndoImport(Dictionary<string, object> changes)
    {
        changes[ownerField] = FieldValue.Delete;
        changes[stateField] = FieldValue.Delete;
    }

    private string? ReadNativeState(IReadOnlyDictionary<string, object> state)
    {
        if (!state.TryGetValue("nativeStateJson", out var value)) return null;
        if (value is not string native || string.IsNullOrWhiteSpace(native) || validateNativeState is null) throw InvalidState();
        validateNativeState(native);
        return native;
    }

    private ClassicSlotRoundState ReadHistorical(IReadOnlyDictionary<string, object> guard, int legacyRemaining,
        long specialPoints, long energy)
    {
        if (specialPoints < 0 || energy < 0) throw InvalidState();
        ClassicSlotRoundCarry? carry = null;
        if (legacyRemaining > 0)
        {
            var cents = Integer(guard, "freeSpinWagerCents");
            if (cents <= 0) cents = checked(Integer(guard, "freeSpinWagerPoints") * CentsPerHistoricalPoint);
            carry = ReadCarry(0, legacyRemaining, cents > 0 ? CentsToPoints(cents) : null, Text(guard, "freeSpinFeatureMode"));
        }

        var hasWagerCents = guard.ContainsKey("sealWagerCents");
        var savedWagers = ReadTracks(guard, hasWagerCents ? "sealWagerCents" : "sealWagerTotals");
        var points = savedWagers.ToDictionary(pair => pair.Key,
            pair => CentsToPoints(hasWagerCents ? pair.Value : checked(pair.Value * CentsPerHistoricalPoint)), StringComparer.Ordinal);
        return new(0, legacyRemaining, carry, checked((int)specialPoints), energy, checked((int)Counter(guard, "consecutiveFiveMisses")),
            ReadTracks(guard, "sealCounts"), points);
    }

    private ClassicSlotRoundCarry? ReadCarry(int nativeRemaining, int legacyRemaining, long? wager, string? mode)
    {
        var remaining = checked(nativeRemaining + legacyRemaining);
        if (remaining == 0) return null;
        if (mode is not null && !SlotProfile.IsFeatureMode(mode)) throw InvalidState();
        if (wager is { } points) validateCarryWager(points);
        // Account migration could create an old free-spin bank before any spin wrote its lock.
        else if (nativeRemaining != 0) throw InvalidState();
        return new(remaining, wager, mode);
    }

    private long CentsToPoints(long cents)
    {
        var points = cents / pointValueInCents;
        if (cents < 0 || points != decimal.Truncate(points)) throw InvalidState();
        return checked((long)points);
    }

    private static IReadOnlyDictionary<string, object> Fields(DocumentSnapshot snapshot) =>
        snapshot.Exists ? snapshot.ToDictionary() : Empty;

    private long LegacyBalance(DocumentSnapshot snapshot, string account, string currency)
    {
        var data = Fields(snapshot);
        if ((Text(data, "userId") is { } owner && owner != account) ||
            (Text(data, "currencyId") is { } currencyId && currencyId != currency))
            throw InvalidState();
        return Counter(data, "available");
    }

    private long Counter(IReadOnlyDictionary<string, object> data, string name, bool required = false)
    {
        var value = Integer(data, name, required);
        return value < 0 ? throw InvalidState() : value;
    }

    private long Integer(IReadOnlyDictionary<string, object> data, string name, bool required = false) =>
        data.GetValueOrDefault(name) switch
        {
            long value => value,
            int value => value,
            null when !required => 0L,
            _ => throw InvalidState(),
        };

    private string? Text(IReadOnlyDictionary<string, object> data, string name) =>
        data.GetValueOrDefault(name) switch
        {
            null => null,
            string value when string.IsNullOrWhiteSpace(value) => null,
            string value => value,
            _ => throw InvalidState(),
        };

    private IReadOnlyDictionary<string, long> ReadTracks(IReadOnlyDictionary<string, object> data, string key,
        bool required = false)
    {
        var map = data.GetValueOrDefault(key) as IReadOnlyDictionary<string, object>;
        if (map is null && required) throw InvalidState();
        return ClassicSlotCollections.TrackIds.ToDictionary(track => track,
            track => map is null ? 0L : required ? Counter(map, track, required: true) :
                HistoricalTrackCounter(map.GetValueOrDefault(track)), StringComparer.Ordinal);
    }

    private long HistoricalTrackCounter(object? raw)
    {
        var value = raw switch
        {
            long integer => integer,
            int integer => integer,
            double number => checked((long)number),
            decimal number => checked((long)number),
            _ => long.TryParse(raw?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L,
        };
        return value < 0 ? throw InvalidState() : value;
    }

    private Dictionary<string, object> WriteTracks(IReadOnlyDictionary<string, long> tracks) =>
        ClassicSlotCollections.TrackIds.ToDictionary(track => track, track =>
        {
            var value = tracks.GetValueOrDefault(track);
            return value < 0 ? throw InvalidState() : (object)value;
        }, StringComparer.Ordinal);

    private InvalidOperationException InvalidState() => new($"The saved {gameName} round state is invalid.");
}
