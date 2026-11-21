namespace FortuneForge.Games.MidnightVault;

public sealed record MidnightVaultDemoCarry(
    IReadOnlyList<MidnightVaultBonusRound> Rounds,
    IReadOnlyDictionary<string, long> CollectionCounts,
    IReadOnlyDictionary<string, long> CollectionWagerPoints);

public static class MidnightVaultDemoPolicy
{
    private const int MaximumQueuedDemoSpins = 1_000;

    public static MidnightVaultDemoCarry ReadLegacyCarry(long wager, int remaining, long? lockedWager,
        string? featureMode, IEnumerable<(string SealId, int Count, long AverageWagerPoints)> collections,
        string? capturedMathModelId)
    {
        // Older live demo state has the v2 contract. Offline replay can supply its captured model.
        var model = capturedMathModelId ?? MidnightVaultSlotDefinitions.PreviousPaytableId;
        if (model is not (MidnightVaultSlotDefinitions.LegacyPaytableId or
            MidnightVaultSlotDefinitions.PreviousPaytableId or MidnightVaultSlotDefinitions.PaytableId))
            throw new ArgumentException("The demo carry has an unsupported Midnight Vault payout model.", nameof(capturedMathModelId));
        // An aggregate v1 carry cannot identify its original six- or seven-spin rounds.
        if (remaining > 0 && model == MidnightVaultSlotDefinitions.LegacyPaytableId)
            throw new ArgumentException("A legacy v1 demo bonus requires its original round state.", nameof(capturedMathModelId));

        var rounds = new List<MidnightVaultBonusRound>();
        for (var index = 0; remaining > 0; index++)
        {
            var spins = Math.Min(MidnightVaultSlotDefinitions.BonusRoundSpins, remaining);
            rounds.Add(new($"demo-carry:{index}", MidnightVaultSlotDefinitions.BonusRoundSpins, spins,
                lockedWager ?? wager, featureMode, model));
            remaining -= spins;
        }
        var counts = collections.ToDictionary(entry => entry.SealId, entry => (long)entry.Count, StringComparer.Ordinal);
        var totals = collections.ToDictionary(entry => entry.SealId,
            entry => checked(entry.Count * (entry.AverageWagerPoints > 0 ? entry.AverageWagerPoints : wager)), StringComparer.Ordinal);
        return new(rounds, counts, totals);
    }

    public static (MidnightVaultBonusRound? Head, long WagerPoints) Admit(IReadOnlyList<MidnightVaultBonusRound> rounds,
        bool useFreeSpin, int freeSpinsRemaining, long requestedWager)
    {
        MidnightVaultBonusRounds.Validate(rounds);
        if (rounds.Sum(round => (long)round.RemainingSpins) > MaximumQueuedDemoSpins)
            throw new ArgumentException("The demo bonus queue exceeds its allowed size.");
        var head = rounds.FirstOrDefault();
        if (freeSpinsRemaining != (head?.RemainingSpins ?? 0) || useFreeSpin != (head is not null))
            throw new ArgumentException("Finish the active demo bonus round before placing another bet.");
        var wager = head?.WagerPoints ?? requestedWager;
        if (requestedWager != wager)
            throw new ArgumentException("The bonus round uses its locked wager.");
        if (head is null) MidnightVaultBonusPolicy.ValidateCashWager(wager);
        else MidnightVaultBonusPolicy.ValidateDerivedWager(wager);
        return (head, wager);
    }

    public static MidnightVaultBonusProgress Advance(IReadOnlyList<MidnightVaultBonusRound> rounds,
        IReadOnlyDictionary<string, long> collectionCounts, IReadOnlyDictionary<string, long> collectionWagerPoints,
        bool useFreeSpin, IReadOnlyDictionary<string, int> awardedSeals, long wager, int scatterSpins, string? scatterMode,
        string model, Guid spinId) =>
        MidnightVaultBonusRounds.Advance(useFreeSpin ? MidnightVaultBonusRounds.Consume(rounds) : rounds,
            collectionCounts, collectionWagerPoints, awardedSeals, wager, scatterSpins, scatterMode, model, spinId);

    public static int CollectionSpinsAwarded(MidnightVaultBonusProgress progress) =>
        progress.AwardedRounds.Where(round => round.FeatureMode == MidnightVaultSlotDefinitions.CollectionFeatureMode)
            .Sum(round => round.TotalSpins);

    public static IReadOnlyList<(string SealId, int Count, long AverageWagerPoints, int RequiredCount)> DescribeCollections(
        IReadOnlyDictionary<string, long> counts, IReadOnlyDictionary<string, long> totals) =>
        MidnightVaultBonusRounds.CollectionTrackIds.Select(mode =>
        {
            var count = counts.GetValueOrDefault(mode);
            var average = count > 0 ? checked((totals.GetValueOrDefault(mode) + count / 2) / count) : 0;
            return (mode, checked((int)count), average, MidnightVaultEngine.Profile.CollectionTarget);
        }).ToArray();
}
