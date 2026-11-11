namespace FortuneForge.Games.MidnightVault;

// A gem-weighted bonus average can be an odd number of native points even though
// cash bets are even. Only a checked server entitlement may use this wider range.
public static class MidnightVaultBonusPolicy
{
    public static bool IsValidDerivedWager(long wager) =>
        wager >= MidnightVaultSlotDefinitions.MinimumWagerPoints &&
        wager <= MidnightVaultSlotDefinitions.MaximumWagerPoints;

    public static void ValidateDerivedWager(long wager)
    {
        if (!IsValidDerivedWager(wager))
            throw new ArgumentOutOfRangeException(nameof(wager), "The bonus wager is outside Midnight Vault's stake bounds.");
    }

    public static void ValidateCashWager(long wager)
    {
        ValidateDerivedWager(wager);
        if ((wager - MidnightVaultSlotDefinitions.MinimumWagerPoints) % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(wager), "That cash wager is not available in Midnight Vault.");
    }

    public static string ResolveModel(string? stored) => stored switch
    {
        null or "" => MidnightVaultSlotDefinitions.LegacyPaytableId,
        MidnightVaultSlotDefinitions.LegacyPaytableId => MidnightVaultSlotDefinitions.LegacyPaytableId,
        MidnightVaultSlotDefinitions.PreviousPaytableId => MidnightVaultSlotDefinitions.PreviousPaytableId,
        MidnightVaultSlotDefinitions.PaytableId => MidnightVaultSlotDefinitions.PaytableId,
        _ => throw new InvalidOperationException("The saved Midnight Vault bonus has an unsupported payout model.")
    };

    public static void ValidateFeatureMode(string mathModelId, string? featureMode)
    {
        _ = ResolveModel(mathModelId);
        if (featureMode is null) return;
        var parts = featureMode.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var valid = mathModelId == MidnightVaultSlotDefinitions.LegacyPaytableId
            ? parts.Length > 0 && parts.All(part => MidnightVaultSlotDefinitions.LegacyFeatureModes.Contains(part, StringComparer.Ordinal))
            : featureMode == MidnightVaultSlotDefinitions.CollectionFeatureMode;
        if (!valid)
            throw new InvalidOperationException("The bonus feature mode is not registered in its model.");
    }
}
