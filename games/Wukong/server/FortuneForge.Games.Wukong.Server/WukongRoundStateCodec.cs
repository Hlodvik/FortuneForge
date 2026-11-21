using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Wukong.Server;

internal sealed record WukongCarry(int Remaining, long? WagerPoints, string? Mode);

internal sealed record WukongRoundBefore(int NativeRemaining, int LegacyRemaining, WukongCarry? Carry,
    int SpecialPoints, long Energy, int Pity, IReadOnlyDictionary<string, long> SealCounts,
    IReadOnlyDictionary<string, long> SealWagerPoints)
{
    internal bool Matches(WukongRoundBefore other) => ToShared().Matches(other.ToShared());

    internal ClassicSlotRoundState ToShared() => new(NativeRemaining, LegacyRemaining,
        Carry is { } carry ? new(carry.Remaining, carry.WagerPoints, carry.Mode) : null,
        SpecialPoints, Energy, Pity, SealCounts, SealWagerPoints);

    internal static WukongRoundBefore FromShared(ClassicSlotRoundState state) =>
        new(state.NativeRemaining, state.LegacyRemaining,
            state.Carry is { } carry ? new(carry.Remaining, carry.WagerPoints, carry.Mode) : null,
            state.SpecialPoints, state.Energy, state.Pity, state.SealCounts, state.SealWagerPoints);
}

public static class WukongRoundStateCodec
{
    public const string OwnerField = "wukongRoundOwner";
    public const string Owner = WukongEngine.GameId;
    internal static readonly ClassicSlotRoundStateCodec Storage = new(WukongEngine.LegacyStorageGameId, OwnerField, Owner,
        "wukongRoundState", "Wukong", usesEnergy: true, pointValueInCents: WukongEngine.WagerPointValueInCents,
        validateCarryWager: static wager => WukongEngine.ValidateWager(wager, isFreeSpin: true));

    internal static async Task<WukongRoundBefore> ReadAsync(Transaction transaction, FirestoreDb database,
        DocumentSnapshot guard, string account, long signedLegacyCount, CancellationToken token) =>
        WukongRoundBefore.FromShared(await Storage.ReadAsync(transaction, database, guard, account, signedLegacyCount, token));

    internal static WukongRoundBefore Read(DocumentSnapshot guard, long expectedLegacyCount) =>
        WukongRoundBefore.FromShared(Storage.Read(guard, expectedLegacyCount));

    internal static WukongRoundBefore Decode(IReadOnlyDictionary<string, object> guard, long signedLegacyCount,
        long legacySpecialPoints = 0, long legacyEnergy = 0) =>
        WukongRoundBefore.FromShared(Storage.Decode(guard, signedLegacyCount, legacySpecialPoints, legacyEnergy));

    internal static Dictionary<string, object> Write(WukongRoundBefore state) => Storage.Write(state.ToShared());

    internal static void UndoImport(Dictionary<string, object> changes) => Storage.UndoImport(changes);
}
