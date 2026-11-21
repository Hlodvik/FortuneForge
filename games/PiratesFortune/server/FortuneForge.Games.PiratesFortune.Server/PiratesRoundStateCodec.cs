using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.PiratesFortune.Server;

internal sealed record PiratesCarry(int Remaining, long? WagerPoints, string? Mode);

internal sealed record PiratesRoundBefore(int NativeRemaining, int LegacyRemaining, PiratesCarry? Carry,
    int SpecialPoints, long Energy, int Pity, IReadOnlyDictionary<string, long> SealCounts,
    IReadOnlyDictionary<string, long> SealWagerPoints)
{
    internal bool Matches(PiratesRoundBefore other) => ToShared().Matches(other.ToShared());

    internal ClassicSlotRoundState ToShared() => new(NativeRemaining, LegacyRemaining,
        Carry is { } carry ? new(carry.Remaining, carry.WagerPoints, carry.Mode) : null,
        SpecialPoints, Energy, Pity, SealCounts, SealWagerPoints);

    internal static PiratesRoundBefore FromShared(ClassicSlotRoundState state) =>
        new(state.NativeRemaining, state.LegacyRemaining,
            state.Carry is { } carry ? new(carry.Remaining, carry.WagerPoints, carry.Mode) : null,
            state.SpecialPoints, state.Energy, state.Pity, state.SealCounts, state.SealWagerPoints);
}

public static class PiratesRoundStateCodec
{
    public const string OwnerField = "piratesFortuneRoundOwner";
    public const string Owner = PiratesFortuneEngine.GameId;
    // Historical energy was captured by the old admission. Pirates' round rules retire it on settlement.
    internal static readonly ClassicSlotRoundStateCodec Storage = new(Owner, OwnerField, Owner,
        "piratesFortuneRoundState", "Pirates’ Fortune", usesEnergy: true, pointValueInCents: PiratesFortuneEngine.WagerPointValueInCents,
        validateCarryWager: static wager =>
        {
            // The old gem reducer could save a one-point lock. Retain it for reconciliation.
            if (wager != 1) PiratesFortuneEngine.ValidateWager(wager, isFreeSpin: true);
        });

    internal static async Task<PiratesRoundBefore> ReadAsync(Transaction transaction, FirestoreDb database,
        DocumentSnapshot guard, string account, long signedLegacyCount, CancellationToken token) =>
        PiratesRoundBefore.FromShared(await Storage.ReadAsync(transaction, database, guard, account, signedLegacyCount, token));

    internal static PiratesRoundBefore Read(DocumentSnapshot guard, long expectedLegacyCount) =>
        PiratesRoundBefore.FromShared(Storage.Read(guard, expectedLegacyCount));

    internal static PiratesRoundBefore Decode(IReadOnlyDictionary<string, object> guard, long signedLegacyCount,
        long legacySpecialPoints = 0, long legacyEnergy = 0) =>
        PiratesRoundBefore.FromShared(Storage.Decode(guard, signedLegacyCount, legacySpecialPoints, legacyEnergy));

    internal static Dictionary<string, object> Write(PiratesRoundBefore state) => Storage.Write(state.ToShared());

    internal static void UndoImport(Dictionary<string, object> changes) => Storage.UndoImport(changes);
}
