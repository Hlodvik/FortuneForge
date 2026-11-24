using System.Diagnostics;
using FortuneForge.Games.CosmicFortune.Server;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.CosmicFortune.RtpAnalysis;

public sealed record ReturnBlock(long PaidSpins, long PaidWagerCents, long PaidReelCents, long EarnedReelCents,
    IReadOnlyDictionary<string, long> TombCents);
public sealed record ProfileResult(long StakePoints, object Totals, object Strategies, object ResidualResources,
    object ConfidenceMethod, IReadOnlyList<ReturnBlock> Blocks, double ElapsedSeconds, double PaidSpinsPerSecond)
{
    public required string WagerPolicy { get; init; }
    public required object[] PayoutContributions { get; init; }
}

internal static class Simulation
{
    internal static ProfileResult Run(MathOptions options, long stake)
    {
        var watch = Stopwatch.StartNew();
        var seedStake = options.PairedStakes ? 0 : stake;
        var reels = new SeededSource(SeededSource.For(options.Seed, seedStake, "reels"));
        var engines = new Dictionary<string, CosmicFortuneEngine>(StringComparer.Ordinal);
        var pityLimit = CosmicFortuneEngine.CreateGame().Math.FiveMatchPityMissLimit;
        var boostCost = CosmicFortuneEngine.CreateGame().SpecialPoints!.ActivationCost;
        var state = new ClassicSlotRoundState(0, 0, null, 0, 0, 0, new Dictionary<string, long>(), new Dictionary<string, long>());
        var policies = options.Strategies.Append("coins").Distinct(StringComparer.Ordinal).ToArray();
        var prizes = policies.ToDictionary(policy => policy, policy => new SeededSource(SeededSource.For(options.Seed, seedStake, "tomb-prizes-" + policy)));
        var decisions = policies.ToDictionary(policy => policy, policy => new SeededSource(SeededSource.For(options.Seed, seedStake, "tomb-decisions-" + policy)));
        var tomb = policies.ToDictionary(policy => policy, _ => new TombTotals());
        var blocks = new List<ReturnBlock>();
        var common = new ReelTotals();
        var contributions = new PayoutContributions();
        long blockPaid = 0, blockWager = 0, blockPaidReturn = 0, blockEarnedReturn = 0;
        var blockTomb = policies.ToDictionary(policy => policy, _ => 0L);
        for (long paid = 0; paid < options.PaidSpins; paid++)
        {
            Resolve(false);
            var earnedThisCycle = 0;
            while (state.Carry is not null)
            {
                if (++earnedThisCycle > 1_000_000) throw new InvalidOperationException("Earned-spin carry did not drain; stopped without deleting entitlements.");
                Resolve(true);
            }
            common.MaximumEarnedSpinsInOnePaidCycle = System.Math.Max(common.MaximumEarnedSpinsInOnePaidCycle, earnedThisCycle);
            var funded = SealedTombState.Parse(state.NativeStateJson);
            while (funded.Tickets.Count > 0)
            {
                if (options.WagerPolicy == "fixed" && funded.Tickets[0].FundedWagerPoints != stake)
                    throw new InvalidOperationException("Flat-stake ticket funding changed its stake.");
                SealedTombState? mainAfter = null;
                foreach (var policy in policies)
                {
                    var trial = TombStrategies.Redeem(funded, policy, prizes[policy], decisions[policy]);
                    var cents = CosmicFortuneRoundPolicy.ToCents(trial.Result.PayoutPoints, CosmicFortuneEngine.WagerPointValueInCents);
                    tomb[policy].Add(trial, cents);
                    blockTomb[policy] = checked(blockTomb[policy] + cents);
                    if (policy == "coins") mainAfter = trial.After;
                }
                funded = mainAfter ?? throw new InvalidOperationException("The persisted comparison branch did not finish its ticket.");
            }
            state = state with { NativeStateJson = SealedTombState.Serialize(funded) };
            blockPaid++;
            if (blockPaid == options.BatchSize || paid + 1 == options.PaidSpins)
            {
                blocks.Add(new(blockPaid, blockWager, blockPaidReturn, blockEarnedReturn,
                    blockTomb.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));
                Console.Error.WriteLine($"stake={stake} paid={paid + 1}/{options.PaidSpins} earned={common.EarnedSpins} elapsed={watch.Elapsed.TotalSeconds:F2}s rate={(paid + 1) / watch.Elapsed.TotalSeconds:F1} paid/s");
                blockPaid = blockWager = blockPaidReturn = blockEarnedReturn = 0;
                foreach (var policy in policies) blockTomb[policy] = 0;
            }
        }
        if (state.Carry is not null || state.NativeRemaining != 0 || state.LegacyRemaining != 0)
            throw new InvalidOperationException("The reported sample has unplayed earned spins.");
        var residual = SealedTombState.Parse(state.NativeStateJson);
        if (residual.Tickets.Count != 0 || residual.ActiveRun is not null || residual.LastRun is not null)
            throw new InvalidOperationException("The reported sample has an unsettled funded tomb ticket.");
        if (state.SpecialPoints != common.SpecialPointsAwarded - common.SpecialPointsSpent)
            throw new InvalidOperationException("Earned boost spending does not reconcile to the saved special-point balance.");
        var strategyReports = options.Strategies.ToDictionary(policy => policy, policy => (object)new
        {
            decisionPolicy = policy == "keys" ? "three coin rooms, then three key rooms, then sarcophagus" : policy,
            tomb = tomb[policy],
            observedTombMeanMultiplier = tomb[policy].FundedWagerPoints == 0 ? (double?)null : tomb[policy].PayoutPoints / (double)tomb[policy].FundedWagerPoints,
            observedRtp = (common.PaidReelPayoutCents + common.EarnedReelPayoutCents + tomb[policy].PayoutCents) / (double)common.PaidWagerCents,
            paidReelRtp = common.PaidReelPayoutCents / (double)common.PaidWagerCents,
            earnedReelRtp = common.EarnedReelPayoutCents / (double)common.PaidWagerCents,
            tombRtp = tomb[policy].PayoutCents / (double)common.PaidWagerCents,
            blockConfidence95 = BlockConfidence.Calculate(blocks.Where(block => block.PaidSpins == options.BatchSize).ToArray(), policy),
        });
        return new(stake, common, strategyReports, new
        {
            state.Energy, state.Pity, state.NativeRemaining, state.LegacyRemaining, state.Carry,
            state.SpecialPoints,
            state.SealCounts, state.SealWagerPoints,
            tombFunding = residual.View().Funding,
            residual.SigilWagerPointTotal,
            unredeemedTickets = residual.Tickets.Count,
            note = "Partial sigils, collections, energy and pity are carried through every block and left unvalued at the final boundary; no new paid spins were manufactured to complete them.",
        }, new { kind = "continuous nonoverlapping paid-cycle block ratio means", options.BatchSize,
            completedBlocks = blocks.Count(block => block.PaidSpins == options.BatchSize),
            note = "Approximate Student-t interval from long blocks, retaining serial game state; a single smoke block has no meaningful confidence interval." },
            blocks, watch.Elapsed.TotalSeconds, options.PaidSpins / watch.Elapsed.TotalSeconds)
        {
            WagerPolicy = options.WagerPolicy,
            PayoutContributions = contributions.Report(common.PaidWagerPoints, common.PaidReelBasePayoutPoints,
                common.EarnedReelBasePayoutPoints, common.PaidReelPayoutPoints, common.EarnedReelPayoutPoints),
        };

        void Resolve(bool free)
        {
            var wager = free ? state.Carry?.WagerPoints ?? throw new InvalidOperationException("A fresh math stream cannot contain an unpriced legacy wager.") :
                options.WagerPolicy switch
                {
                    "fixed" => stake,
                    "sunfire-switch" => state.Energy >= ClassicSlotEnergy.MeterCapacityPoints ? stake : PharaohsAscentWagering.Current.MinimumWagerPoints,
                    "sunfire-anticipate" => state.Energy >= ClassicSlotEnergy.MeterCapacityPoints - 10 ? stake : PharaohsAscentWagering.Current.MinimumWagerPoints,
                    "pity-switch" => pityLimit is { } limit && state.Pity >= limit ? stake : PharaohsAscentWagering.Current.MinimumWagerPoints,
                    "progression-switch" => state.Energy >= ClassicSlotEnergy.MeterCapacityPoints ||
                        pityLimit is { } combinedLimit && state.Pity >= combinedLimit ? stake : PharaohsAscentWagering.Current.MinimumWagerPoints,
                    "alternating" => common.PaidSpins % 2 == 0 ? PharaohsAscentWagering.Current.MinimumWagerPoints : stake,
                    _ => throw new InvalidOperationException("Unknown paid wager policy."),
                };
            if (!free) PharaohsAscentWagering.ValidateNewPaidWager(wager);
            var version = CosmicFortuneRoundPolicy.EngineVersionForSpin(state, free);
            if (!engines.TryGetValue(version, out var engine)) engines[version] = engine = new CosmicFortuneEngine(reels, version);
            var pity = CosmicFortuneRoundPolicy.PityBeforeSpin(state, wager, version);
            var appearanceEnergy = CosmicFortuneRoundPolicy.EnergyBeforeSpin(state, wager, version);
            var useBoost = options.UseEarnedBoosts && CosmicFortuneRoundPolicy.BoostPointsAvailable(state, wager, version) >= boostCost;
            var outcome = engine.Spin(wager, "pharaoh-rtp-stream", useBoost, appearanceEnergy, free ? state.Carry?.Mode : null, pity, free);
            var progress = CosmicFortuneRoundPolicy.Advance(state, outcome, CosmicFortuneRoundPolicy.SpendsLegacy(state, free));
            var settled = progress.SettledPayout.TotalPoints;
            var cents = CosmicFortuneRoundPolicy.ToCents(settled, outcome.PointValueInCents);
            var energyExtra = checked(settled - outcome.Payout.TotalPoints);
            if (energyExtra < 0) throw new InvalidOperationException("Energy settlement reduced a spin payout.");
            contributions.Add(outcome, progress.SettledPayout, progress.EnergySettlement.MultiplierApplied,
                progress.EnergySettlement.PayoutMultiplier, free);
            if (free)
            {
                common.EarnedSpins++;
                if (useBoost) common.EarnedBoostSpins++;
                if (wager % 2 != 0) common.OddFundedEarnedSpins++;
                common.EarnedReelBasePayoutPoints += outcome.Payout.TotalPoints;
                common.EarnedReelPayoutPoints += settled;
                common.EarnedReelPayoutCents += cents;
                common.EarnedEnergyExtraPoints += energyExtra;
                blockEarnedReturn += cents;
            }
            else
            {
                var charged = CosmicFortuneRoundPolicy.ToCents(wager, outcome.PointValueInCents);
                common.PaidSpins++;
                if (useBoost) common.PaidBoostSpins++;
                if (wager == PharaohsAscentWagering.Current.MinimumWagerPoints) common.MinimumBetPaidSpins++;
                if (wager == stake) common.SelectedHighBetPaidSpins++;
                common.PaidWagerPoints += wager;
                common.PaidWagerCents += charged;
                common.PaidReelBasePayoutPoints += outcome.Payout.TotalPoints;
                common.PaidReelPayoutPoints += settled;
                common.PaidReelPayoutCents += cents;
                common.PaidEnergyExtraPoints += energyExtra;
                blockWager += charged;
                blockPaidReturn += cents;
                var tombBefore = SealedTombState.Parse(state.NativeStateJson);
                var tombAfter = SealedTombState.Parse(progress.After.NativeStateJson);
                if (tombAfter.Sigils != tombBefore.Sigils || tombAfter.Tickets.Count > tombBefore.Tickets.Count)
                    common.PaidSigilQualifyingSpins++;
                if (progress.EnergySettlement.MultiplierApplied)
                {
                    common.PaidEnergyMultiplierEvents++;
                    common.PaidEnergyMultiplierWagerPoints += wager;
                }
            }
            if (outcome.FiveMatchPityTriggered)
            {
                common.PityTriggeredSpins++;
                if (!free)
                {
                    common.PaidPityTriggeredSpins++;
                    common.PaidPityTriggeredWagerPoints += wager;
                }
            }
            common.GenerationAttempts += outcome.Audit.GenerationAttempts;
            common.ScatterSpinsAwarded += outcome.FreeSpinsAwarded;
            common.SpecialPointsAwarded += outcome.SpecialPointsAwarded;
            common.CollectionSpinsAwarded += progress.CollectionFreeSpinsAwarded;
            if (progress.EnergySettlement.MultiplierApplied) common.EnergyMultiplierEvents++;
            if (useBoost) common.SpecialPointsSpent += boostCost;
            state = progress.After;
        }
    }
}

public sealed class ReelTotals
{
    public long PaidSpins { get; set; }
    public long EarnedSpins { get; set; }
    public long PaidWagerPoints { get; set; }
    public long PaidWagerCents { get; set; }
    public long PaidReelBasePayoutPoints { get; set; }
    public long PaidReelPayoutPoints { get; set; }
    public long PaidReelPayoutCents { get; set; }
    public long PaidEnergyExtraPoints { get; set; }
    public long EarnedReelBasePayoutPoints { get; set; }
    public long EarnedReelPayoutPoints { get; set; }
    public long EarnedReelPayoutCents { get; set; }
    public long EarnedEnergyExtraPoints { get; set; }
    public long PaidSigilQualifyingSpins { get; set; }
    public long PityTriggeredSpins { get; set; }
    public long PaidPityTriggeredSpins { get; set; }
    public long PaidPityTriggeredWagerPoints { get; set; }
    public long GenerationAttempts { get; set; }
    public long ScatterSpinsAwarded { get; set; }
    public long CollectionSpinsAwarded { get; set; }
    public long EnergyMultiplierEvents { get; set; }
    public long MaximumEarnedSpinsInOnePaidCycle { get; set; }
    public long OddFundedEarnedSpins { get; set; }
    public long MinimumBetPaidSpins { get; set; }
    public long SelectedHighBetPaidSpins { get; set; }
    public long PaidEnergyMultiplierEvents { get; set; }
    public long PaidEnergyMultiplierWagerPoints { get; set; }
    public long PaidBoostSpins { get; set; }
    public long EarnedBoostSpins { get; set; }
    public long SpecialPointsSpent { get; set; }
    public long SpecialPointsAwarded { get; set; }
}

public sealed class TombTotals
{
    public long Expeditions { get; set; }
    public long Actions { get; set; }
    public long RoomsOpened { get; set; }
    public long BasePoints { get; set; }
    public long RoomPoints { get; set; }
    public long RelicExtraPoints { get; set; }
    public long FinalePoints { get; set; }
    public long PayoutPoints { get; set; }
    public long PayoutCents { get; set; }
    public long FundedWagerPoints { get; set; }
    internal void Add(TombTrial trial, long cents)
    {
        Expeditions++;
        FundedWagerPoints += trial.Result.FundedWagerPoints;
        Actions += trial.Actions;
        RoomsOpened += trial.Result.RoomsOpened;
        BasePoints += trial.Result.BasePoints;
        RoomPoints += trial.Result.RoomPoints;
        RelicExtraPoints += trial.Result.RoomPoints * (trial.Result.RelicMultiplier - 1);
        FinalePoints += trial.Result.FinalePoints;
        PayoutPoints += trial.Result.PayoutPoints;
        PayoutCents += cents;
    }
}
