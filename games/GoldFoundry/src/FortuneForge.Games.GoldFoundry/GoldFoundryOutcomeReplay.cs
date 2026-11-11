using System.Text.Json;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.GoldFoundry;

/// <summary>Independent replay of current package-owned Gold Foundry evidence.</summary>
public static class GoldFoundryOutcomeReplay
{
    public static IReadOnlyList<GoldFoundryReplayFinding> Check(
        GoldFoundrySpinOutcome result, SpinPayout settledPayout)
    {
        var findings = new List<GoldFoundryReplayFinding>();
        void Flag(string code, string message) => findings.Add(new(code, message));
        var audit = result.EngineAudit;
        if (audit.EngineVersion != GoldFoundryEngine.Version)
        {
            Flag("slot-foundry-version-mismatch", "Gold Foundry evidence uses an unsupported owned engine version.");
            return findings;
        }
        if (audit.CrucibleRuleVersion != GoldFoundryCrucibleRules.RuleVersion)
            Flag("slot-crucible-rule-mismatch", "The saved crucible rule does not match Gold Foundry's package rule.");

        var expectedGame = GoldFoundrySlotDefinitions.CreateGame();
        var expectedSymbols = GoldFoundrySlotDefinitions.CreateSymbolSet();
        var expectedReels = GoldFoundrySlotDefinitions.CreateReelSet();
        var expectedPaytable = GoldFoundrySlotDefinitions.CreatePaytable();
        if (!Same(audit.BaseGame, expectedGame) || !Same(audit.EffectiveGame, expectedGame) ||
            !Same(audit.SymbolSet, expectedSymbols) || !Same(audit.ReelSet, expectedReels) ||
            !Same(audit.Paytable, expectedPaytable) ||
            !Same(audit.FeatureProfile, GoldFoundrySlotDefinitions.Profile))
            Flag("slot-foundry-rules-mismatch", "The captured rules differ from Gold Foundry's package model.");
        if (result.GameId != GoldFoundrySlotDefinitions.GameId || result.GameId != audit.BaseGame.Id ||
            result.ReelSetId != audit.ReelSet.Id || result.SymbolSetId != audit.SymbolSet.Id ||
            result.PaytableId != audit.Paytable.Id ||
            result.PointValueInCents != audit.BaseGame.Wagering.PointValueInCents)
            Flag("slot-foundry-identity-mismatch", "The outcome identifiers do not match the captured Gold Foundry rules.");
        if (result.IsFreeSpin || audit.IsDerivedFreeSpin || result.SpecialBoostApplied ||
            audit.FreeSpinFeatureMode is not null || audit.EnergyBalanceBefore < 0 ||
            result.FreeSpinsAwarded != 0 || result.SpecialPointsAwarded != 0 || result.SealsAwarded.Count != 0)
            Flag("slot-foundry-input-mismatch", "The outcome contains inputs or awards Gold Foundry does not support.");
        try { new GoldFoundryEngine(new NoDrawRandom()).ValidateRequest(result.WagerPoints); }
        catch (ArgumentOutOfRangeException)
        {
            Flag("slot-foundry-wager-invalid", "The wager does not match Gold Foundry's package rules.");
        }

        if (result.Reels.Count != audit.BaseGame.Layout.ReelCount ||
            result.Reels.Any(reel => reel.Count != audit.BaseGame.Layout.VisibleRows) ||
            result.ReelStops.Count != audit.BaseGame.Layout.ReelCount ||
            audit.RawGeneratedReels.Count != audit.BaseGame.Layout.ReelCount ||
            audit.ReelsAfterFreeGameFrequency.Count != audit.BaseGame.Layout.ReelCount ||
            audit.ReelSet.Reels.Count != audit.BaseGame.Layout.ReelCount)
        {
            Flag("slot-foundry-grid-invalid", "The captured Gold Foundry reel window has an invalid shape.");
            return findings;
        }

        var known = audit.SymbolSet.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        if (result.Reels.SelectMany(reel => reel).Any(symbol => !known.Contains(symbol)))
            Flag("slot-foundry-symbol-invalid", "The outcome contains a symbol outside Gold Foundry's symbol set.");
        for (var reel = 0; reel < result.ReelStops.Count; reel++)
        {
            var strip = audit.ReelSet.Reels[reel];
            var stop = result.ReelStops[reel];
            if (stop < 0 || stop >= strip.Count)
            {
                Flag("slot-foundry-stop-invalid", "A captured Gold Foundry reel stop is out of range.");
                continue;
            }
            var replayed = Enumerable.Range(0, audit.BaseGame.Layout.VisibleRows)
                .Select(row => strip[(stop + row) % strip.Count]);
            if (!replayed.SequenceEqual(audit.RawGeneratedReels[reel]) ||
                !audit.RawGeneratedReels[reel].SequenceEqual(audit.ReelsAfterFreeGameFrequency[reel]) ||
                !audit.RawGeneratedReels[reel].SequenceEqual(result.Reels[reel]))
                Flag("slot-foundry-reel-mismatch", "The Gold Foundry window differs from its captured reel stops.");
        }

        try
        {
            var evaluations = GoldFoundryEngine.Evaluate(result.Reels, audit.EffectiveGame, audit.SymbolSet);
            var expectedPayout = GoldFoundryEngine.CalculatePayout(evaluations, audit.EffectiveGame,
                audit.Paytable, result.WagerPoints);
            var (collectorCount, collectorPoints) = GoldFoundryCollector.Calculate(result.Reels, result.WagerPoints);
            var hammers = GoldFoundryCollector.CalculateHammerPayouts(result.Reels, result.WagerPoints);
            expectedPayout = new SpinPayout(
                checked(expectedPayout.TotalPoints + collectorPoints + hammers.Sum(line => line.AmountPoints)),
                [.. expectedPayout.Paylines, .. hammers]);
            if (!Same(result.Payout, expectedPayout) || !Same(settledPayout, expectedPayout))
                Flag("slot-foundry-payout-mismatch", "The saved payout differs from Gold Foundry's package calculation.");
            if (result.CollectorCount != collectorCount || result.CollectorPayoutPoints != collectorPoints)
                Flag("slot-feature-award-mismatch", "The saved collector award differs from the visible ingots.");
            if (result.HammerBonusPoints != hammers.Sum(line => line.AmountPoints))
                Flag("slot-foundry-hammer-mismatch", "The saved hammer award differs from the visible patterns.");
            var energy = result.Reels.Sum(reel => reel.Count(symbol =>
                symbol == audit.BaseGame.Energy!.SymbolId)) * audit.BaseGame.Energy!.PointsPerVisibleSymbol;
            if (result.EnergyAwarded != energy)
                Flag("slot-energy-award-mismatch", "The saved crucible charge differs from the visible coal.");
            var full = GoldFoundryEngine.HasPayingFullMatch(evaluations, audit.EffectiveGame, audit.Paytable);
            var misses = full ? 0 : checked(audit.PityMissesBefore + 1);
            if (result.ConsecutiveFiveMisses != misses || result.FiveMatchPityTriggered ||
                audit.GenerationAttempts != 1)
                Flag("slot-pity-state-mismatch", "The saved pity transition differs from the evaluated outcome.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            Flag("slot-foundry-replay-invalid", "The captured Gold Foundry outcome cannot be replayed.");
        }
        return findings;
    }

    private static bool Same<T>(T left, T right) =>
        JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    private sealed class NoDrawRandom : IGameRandomSource
    {
        public int Next(int maximumExclusive) => throw new InvalidOperationException("Replay validation must not draw.");
    }
}
