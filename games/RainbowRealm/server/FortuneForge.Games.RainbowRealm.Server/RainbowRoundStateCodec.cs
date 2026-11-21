using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.RainbowRealm.Server;

internal sealed record RainbowCarry(int Remaining, long? WagerPoints, string? Mode);

internal sealed record RainbowRoundBefore(int NativeRemaining, int LegacyRemaining, RainbowCarry? Carry,
    int SpecialPoints, int Pity, IReadOnlyDictionary<string, long> SealCounts,
    IReadOnlyDictionary<string, long> SealWagerPoints)
{
    internal bool Matches(RainbowRoundBefore other) => ToShared().Matches(other.ToShared());

    internal ClassicSlotRoundState ToShared() => new(NativeRemaining, LegacyRemaining,
        Carry is { } carry ? new(carry.Remaining, carry.WagerPoints, carry.Mode) : null,
        SpecialPoints, 0, Pity, SealCounts, SealWagerPoints);

    internal static RainbowRoundBefore FromShared(ClassicSlotRoundState state) =>
        new(state.NativeRemaining, state.LegacyRemaining,
            state.Carry is { } carry ? new(carry.Remaining, carry.WagerPoints, carry.Mode) : null,
            state.SpecialPoints, state.Pity, state.SealCounts, state.SealWagerPoints);
}

public static class RainbowRoundStateCodec
{
    public const string OwnerField = "rainbowRealmRoundOwner";
    public const string Owner = RainbowRealmEngine.GameId;
    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "rainbowRealmRoundState", "Rainbow Realm", usesEnergy: false, pointValueInCents: RainbowRealmEngine.WagerPointValueInCents,
        validateCarryWager: static wager => RainbowRealmEngine.ValidateWager(wager, isFreeSpin: true));

    internal static async Task<RainbowRoundBefore> ReadAsync(Transaction transaction, FirestoreDb database,
        DocumentSnapshot guard, string account, long signedLegacyCount, CancellationToken token) =>
        RainbowRoundBefore.FromShared(await Storage.ReadAsync(transaction, database, guard, account, signedLegacyCount, token));

    internal static RainbowRoundBefore Read(DocumentSnapshot guard, long expectedLegacyCount) =>
        RainbowRoundBefore.FromShared(Storage.Read(guard, expectedLegacyCount));

    internal static RainbowRoundBefore Decode(IReadOnlyDictionary<string, object> guard, long signedLegacyCount,
        long legacySpecialPoints = 0) =>
        RainbowRoundBefore.FromShared(Storage.Decode(guard, signedLegacyCount, legacySpecialPoints));

    internal static Dictionary<string, object> Write(RainbowRoundBefore state) => Storage.Write(state.ToShared());

    internal static void UndoImport(Dictionary<string, object> changes) => Storage.UndoImport(changes);
}
