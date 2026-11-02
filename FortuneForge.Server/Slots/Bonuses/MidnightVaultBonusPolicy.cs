using FortuneForge.Server.Slots.Configuration;

namespace FortuneForge.Server.Slots.Bonuses;

// A gem-weighted bonus average can be an odd number of native points even though
// cash bets are even. Only a checked server entitlement may use this wider range.
internal static class MidnightVaultBonusPolicy
{
    internal static bool IsValidDerivedWager(long wager) =>
        wager >= MidnightVaultSlotDefinitions.MinimumWagerPoints &&
        wager <= MidnightVaultSlotDefinitions.MaximumWagerPoints;

    internal static void ValidateDerivedWager(long wager)
    {
        if (!IsValidDerivedWager(wager))
            throw new ArgumentOutOfRangeException(nameof(wager), "The bonus wager is outside Midnight Vault's stake bounds.");
    }

    internal static void ValidateCashWager(long wager)
    {
        ValidateDerivedWager(wager);
        if ((wager - MidnightVaultSlotDefinitions.MinimumWagerPoints) % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(wager), "That cash wager is not available in Midnight Vault.");
    }

    internal static string ResolveModel(string? stored) => stored switch
    {
        null or "" => MidnightVaultSlotDefinitions.LegacyPaytableId,
        MidnightVaultSlotDefinitions.LegacyPaytableId => MidnightVaultSlotDefinitions.LegacyPaytableId,
        MidnightVaultSlotDefinitions.PaytableId => MidnightVaultSlotDefinitions.PaytableId,
        _ => throw new InvalidOperationException("The saved Midnight Vault bonus has an unsupported payout model.")
    };
}
