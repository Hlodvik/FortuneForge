using FortuneForge.Games.Slots;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.GoldFoundry.Server;

internal sealed record GoldFoundryRoundBefore(long Energy, long CrucibleWageredCents, int Pity);

/// <summary>Imports the original crucible and pity once and keeps subsequent progress in Gold Foundry.</summary>
public static class GoldFoundryRoundStateCodec
{
    public const string OwnerField = "goldFoundryRoundOwner";
    public const string Owner = GoldFoundryEngine.GameId;
    private const string StateField = "goldFoundryRoundState";
    private const long SchemaVersion = 1;
    private const string LegacyEnergyCurrency = "energy:" + GoldFoundryEngine.GameId;
    private static readonly IReadOnlyDictionary<string, object> Empty = new Dictionary<string, object>();

    internal static async Task<GoldFoundryRoundBefore> ReadAsync(Transaction transaction, FirestoreDb database,
        DocumentSnapshot guard, string account, CancellationToken token)
    {
        var guardId = SlotSpinGuard.DocumentId(account, GoldFoundryEngine.GameId);
        if (!guard.Reference.Equals(database.Collection(SlotSpinGuard.Collection).Document(guardId)))
            throw InvalidState();

        var fields = Fields(guard);
        if (Text(fields, OwnerField) is not null)
            return Decode(fields);

        // The legacy charge is a game unit. Gold Foundry fetches cash state and settles only through the wallet port.
        var original = database.Collection("userBalances").Document($"{account}_{LegacyEnergyCurrency}");
        var energy = Fields(await transaction.GetSnapshotAsync(original, token));
        if ((Text(energy, "userId") is { } userId && userId != account) ||
            (Text(energy, "currencyId") is { } currency && currency != LegacyEnergyCurrency))
            throw InvalidState();
        return Decode(fields, Counter(energy, "available"));
    }

    internal static GoldFoundryRoundBefore Read(DocumentSnapshot guard)
    {
        var fields = Fields(guard);
        if (Text(fields, OwnerField) != Owner) throw InvalidState();
        return Decode(fields);
    }

    internal static GoldFoundryRoundBefore Decode(IReadOnlyDictionary<string, object> guard, long legacyEnergy = 0)
    {
        var owner = Text(guard, OwnerField);
        if (owner is null)
        {
            if (guard.ContainsKey(StateField) || legacyEnergy < 0) throw InvalidState();
            return new(legacyEnergy, Counter(guard, "crucibleWageredCents"), checked((int)Counter(guard, "consecutiveFiveMisses")));
        }
        if (owner != Owner || guard.GetValueOrDefault(StateField) is not IReadOnlyDictionary<string, object> state ||
            Counter(state, "version", required: true) != SchemaVersion)
            throw InvalidState();
        return new(Counter(state, "energy", required: true), Counter(state, "crucibleWageredCents", required: true),
            checked((int)Counter(state, "pity", required: true)));
    }

    internal static Dictionary<string, object> Write(GoldFoundryRoundBefore state)
    {
        if (state is { Energy: < 0 } or { CrucibleWageredCents: < 0 } or { Pity: < 0 })
            throw InvalidState();
        return new()
        {
            [OwnerField] = Owner,
            [StateField] = new Dictionary<string, object>
            {
                ["version"] = SchemaVersion,
                ["energy"] = state.Energy,
                ["crucibleWageredCents"] = state.CrucibleWageredCents,
                ["pity"] = (long)state.Pity,
            },
        };
    }

    private static IReadOnlyDictionary<string, object> Fields(DocumentSnapshot snapshot) =>
        snapshot.Exists ? snapshot.ToDictionary() : Empty;

    private static long Counter(IReadOnlyDictionary<string, object> state, string name, bool required = false) =>
        state.GetValueOrDefault(name) switch
        {
            long value when value >= 0 => value,
            int value when value >= 0 => value,
            null when !required => 0L,
            _ => throw InvalidState(),
        };

    private static string? Text(IReadOnlyDictionary<string, object> state, string name) =>
        state.GetValueOrDefault(name) switch
        {
            null => null,
            string value when string.IsNullOrWhiteSpace(value) => null,
            string value => value,
            _ => throw InvalidState(),
        };

    private static InvalidOperationException InvalidState() => new("The saved Gold Foundry crucible state is invalid.");
}
