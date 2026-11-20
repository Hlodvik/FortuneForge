namespace FortuneForge.Games.NeonNights;

public sealed record NeonAwardedPlayState(long Remaining, long? LockedWagerPoints, string? FeatureMode);

public sealed class NeonDiscoWagerLockedException(long lockedWagerPoints)
    : ArgumentException("Disco Plays use the wager from the triggering spin.")
{
    public long LockedWagerPoints { get; } = lockedWagerPoints;
}

// These transitions describe Neon entitlements. Wallet debits and atomic
// persistence are performed by the host through its existing ledger service.
public static class NeonPlayPolicy
{
    public static bool IsFeatureMode(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is { Length: > 0 } parts &&
        parts.All(part => part is "sync" or "rows" or "paw" or "rand" or "arcade" or "car");

    public static bool HasFeatureMode(string? value, string mode) =>
        IsFeatureMode(value) &&
        value!.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(mode, StringComparer.Ordinal);

    public static int NextDiscoSpins(int current, bool consumedDiscoPlay, NeonArcadeEvidence? evidence)
    {
        var grant = evidence is null || evidence.ModelId == NeonArcadeSlotDefinitions.ModelId
            ? NeonArcadeSlotDefinitions.DiscoPlaySpins
            : NeonArcadeSlotDefinitions.PreviousDiscoPlaySpins;
        if (current < 0 || current > grant)
            throw new ArgumentOutOfRangeException(nameof(current));
        return evidence?.Activators.Any(activator => activator.SymbolId == "POWER") == true
            ? grant
            : Math.Max(0, current - (consumedDiscoPlay ? 1 : 0));
    }

    public static bool ChargesWager(bool usesAwardedPlay, string? featureMode) =>
        !usesAwardedPlay || NeonArcadeBonus.IsPaidArcadeMode(featureMode);

    public static long? DiscoLockedWager(int spinsRemaining, long? lockedWagerPoints)
    {
        if (spinsRemaining < 0 || spinsRemaining > NeonArcadeSlotDefinitions.DiscoPlaySpins)
            throw new ArgumentOutOfRangeException(nameof(spinsRemaining));
        if (spinsRemaining == 0) return null;
        if (lockedWagerPoints is { } wager) NeonArcadeSlotDefinitions.ValidateCarryWager(wager);
        return lockedWagerPoints;
    }

    public static void ValidateDiscoWager(int spinsRemaining, long? lockedWagerPoints, long wagerPoints)
    {
        if (DiscoLockedWager(spinsRemaining, lockedWagerPoints) is { } locked && locked != wagerPoints)
            throw new NeonDiscoWagerLockedException(locked);
    }

    public static long? NextDiscoWager(int currentSpins, long? lockedWagerPoints,
        bool consumedDiscoPlay, long wagerPoints, NeonArcadeEvidence? evidence)
    {
        if (NextDiscoSpins(currentSpins, consumedDiscoPlay, evidence) == 0) return null;
        if (DiscoLockedWager(currentSpins, lockedWagerPoints) is { } locked) return locked;
        // Old pending rounds have no saved lock. Adopt their first Disco wager,
        // leaving it unset while a queued Car round is still being played.
        if (currentSpins > 0 && !consumedDiscoPlay) return null;
        NeonArcadeSlotDefinitions.ValidateCarryWager(wagerPoints);
        return wagerPoints;
    }

    public static int NextDiscoMultiplier(int current, int spinsRemaining, NeonDiscoCascadeOutcome? cascade,
        NeonArcadeEvidence? evidence)
    {
        if (spinsRemaining == 0 || evidence?.Activators.Any(activator => activator.SymbolId == "POWER") == true)
            return NeonDiscoCascade.InitialMultiplier;
        return cascade is null ? current : NeonDiscoCascade.MultiplierAfterSpin(current, cascade.Steps.Count);
    }

    public static NeonAwardedPlayState AdvanceAwardedPlays(long remainingAfterConsumption,
        bool usedAwardedPlay, string? previousFeatureMode, int awardedPlays,
        long wagerPoints, NeonArcadeEvidence? evidence)
    {
        if (remainingAfterConsumption < 0 || awardedPlays < 0)
            throw new ArgumentOutOfRangeException(nameof(remainingAfterConsumption));
        var remaining = checked(remainingAfterConsumption + awardedPlays);
        return new(remaining, remaining > 0 ? wagerPoints : null,
            remaining == 0 ? null : usedAwardedPlay ? previousFeatureMode :
            awardedPlays > 0 ? NeonArcadeBonus.AwardedFeatureMode(evidence) : null);
    }
}
