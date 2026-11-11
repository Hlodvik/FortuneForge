using System.Text.Json;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.MidnightVault;

/// <summary>Independent replay of a captured Midnight Vault outcome.</summary>
public static class MidnightVaultOutcomeReplay
{
    public static IReadOnlyList<MidnightVaultReplayFinding> Check(
        MidnightVaultSpinOutcome result, SpinPayout settledPayout)
    {
        var findings = new List<MidnightVaultReplayFinding>();
        void Flag(string code, string message) => findings.Add(new(code, message));
        var audit = result.EngineAudit;
        if (audit.EngineVersion != MidnightVaultEngine.Version)
        {
            Flag("slot-midnight-version-mismatch", "Midnight Vault evidence uses an unsupported owned engine version.");
            return findings;
        }

        GameDefinition expectedBase;
        GameDefinition expectedEffective;
        ReelSetDefinition expectedReels;
        SymbolSetDefinition expectedSymbols;
        PaytableDefinition expectedPaytable;
        try
        {
            expectedBase = MidnightVaultSlotDefinitions.CreateGameForPaytable(result.PaytableId);
            MidnightVaultBonusPolicy.ValidateFeatureMode(result.PaytableId, audit.FreeSpinFeatureMode);
            expectedEffective = MidnightVaultEngine.CreateEffectiveGame(expectedBase, audit.FreeSpinFeatureMode);
            expectedSymbols = MidnightVaultSlotDefinitions.CreateSymbolSet();
            expectedPaytable = MidnightVaultSlotDefinitions.CreatePaytable(result.PaytableId);
            expectedReels = MidnightVaultBaseReelSet.CreateForPaytable(result.PaytableId);
            if (result.SpecialBoostApplied)
                expectedReels = MidnightVaultSpecialPoints.CreateBoostedReelSet(expectedBase, expectedReels);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            Flag("slot-midnight-rules-invalid", "The captured Midnight Vault model or feature mode is invalid.");
            return findings;
        }

        if (!Same(audit.BaseGame, expectedBase) || !Same(audit.EffectiveGame, expectedEffective) ||
            !Same(audit.SymbolSet, expectedSymbols) || !Same(audit.Paytable, expectedPaytable) ||
            !Same(audit.ReelSet, expectedReels) ||
            !Same(audit.FeatureProfile, MidnightVaultSlotDefinitions.CreateProfile(result.PaytableId)))
            Flag("slot-midnight-rules-mismatch", "The captured rules differ from Midnight Vault's frozen package model.");
        if (audit.BaseGame.Math.PaytableId != audit.Paytable.Id ||
            audit.BaseGame.Math.PayoutMultiplier != expectedBase.Math.PayoutMultiplier)
            Flag("slot-model-mismatch", "Midnight Vault's frozen payout model is inconsistent.");

        if (result.GameId != MidnightVaultEngine.GameId || result.GameId != audit.BaseGame.Id ||
            result.SymbolSetId != audit.SymbolSet.Id || result.PaytableId != audit.Paytable.Id ||
            result.ReelSetId != audit.ReelSet.Id || result.PointValueInCents != audit.BaseGame.Wagering.PointValueInCents)
            Flag("slot-midnight-identity-mismatch", "The outcome identifiers do not match the captured Midnight Vault rules.");
        if (result.IsFreeSpin != audit.IsDerivedFreeSpin || audit.EnergyBalanceBefore < 0 || result.EnergyAwarded != 0)
            Flag("slot-midnight-input-mismatch", "The awarded-play or energy evidence is inconsistent.");
        try
        {
            if (result.IsFreeSpin) MidnightVaultBonusPolicy.ValidateDerivedWager(result.WagerPoints);
            else MidnightVaultBonusPolicy.ValidateCashWager(result.WagerPoints);
        }
        catch (ArgumentOutOfRangeException)
        {
            Flag("slot-midnight-wager-invalid", "The wager does not match Midnight Vault's cash or trusted bonus rules.");
        }

        if (result.Reels.Count != audit.EffectiveGame.Layout.ReelCount ||
            result.Reels.Any(reel => reel.Count != audit.EffectiveGame.Layout.VisibleRows) ||
            result.ReelStops.Count != audit.EffectiveGame.Layout.ReelCount ||
            audit.RawGeneratedReels.Count != audit.EffectiveGame.Layout.ReelCount ||
            audit.ReelsAfterFreeGameFrequency.Count != audit.EffectiveGame.Layout.ReelCount)
        {
            Flag("slot-midnight-grid-invalid", "The captured Midnight Vault reel window has an invalid shape.");
            return findings;
        }

        var known = audit.SymbolSet.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        if (result.Reels.SelectMany(reel => reel).Any(symbol => !known.Contains(symbol)))
            Flag("slot-midnight-symbol-invalid", "The outcome contains a symbol outside Midnight Vault's symbol set.");
        for (var reel = 0; reel < result.ReelStops.Count; reel++)
        {
            var strip = audit.ReelSet.Reels[reel];
            var stop = result.ReelStops[reel];
            if (stop < 0 || stop >= strip.Count)
            {
                Flag("slot-midnight-stop-invalid", "A captured Midnight Vault reel stop is out of range.");
                continue;
            }
            var replayed = Enumerable.Range(0, audit.EffectiveGame.Layout.VisibleRows)
                .Select(row => strip[(stop + row) % strip.Count]);
            if (!replayed.SequenceEqual(audit.RawGeneratedReels[reel]))
                Flag("slot-midnight-raw-reel-mismatch", "The raw Midnight Vault reel window differs from its captured stops.");
            if (audit.RawGeneratedReels[reel].Count != audit.ReelsAfterFreeGameFrequency[reel].Count)
            {
                Flag("slot-midnight-frequency-grid-mismatch", "FREE-frequency evidence changed the grid shape.");
                continue;
            }
            for (var row = 0; row < audit.RawGeneratedReels[reel].Count; row++)
            {
                var before = audit.RawGeneratedReels[reel][row];
                var after = audit.ReelsAfterFreeGameFrequency[reel][row];
                if (before == "FREE" ? after is not ("FREE" or "2" or "3" or "4") : after != before)
                    Flag("slot-midnight-frequency-mismatch", "FREE-frequency evidence contains an invalid replacement.");
            }
        }

        try
        {
            var evaluations = MidnightVaultEngine.Evaluate(result.Reels, audit.EffectiveGame, audit.SymbolSet);
            var expectedPayout = MidnightVaultEngine.CalculatePayout(evaluations, audit.EffectiveGame,
                audit.Paytable, result.WagerPoints);
            var bananas = MidnightVaultEngine.CalculateBananaPayouts(result.Reels, result.WagerPoints,
                audit.BaseGame.Math.PayoutMultiplier);
            if (bananas.Count > 0)
                expectedPayout = new SpinPayout(checked(expectedPayout.TotalPoints + bananas.Sum(line => line.AmountPoints)),
                    [.. expectedPayout.Paylines, .. bananas]);
            if (!Same(result.Payout, expectedPayout) || !Same(settledPayout, expectedPayout))
                Flag("slot-midnight-payout-mismatch", "The saved payout differs from Midnight Vault's owned calculation.");
            if (result.BananaBonusPoints != bananas.Sum(line => line.AmountPoints))
                Flag("slot-midnight-banana-mismatch", "The saved pattern payout differs from the visible banana patterns.");
            var freeSpins = MidnightVaultEngine.CountSymbols(result.Reels, audit.BaseGame.FreeGames!.SymbolId) >=
                audit.BaseGame.FreeGames.RequiredSymbols ? audit.BaseGame.FreeGames.AwardedSpins : 0;
            if (result.FreeSpinsAwarded != freeSpins)
                Flag("slot-midnight-free-game-mismatch", "The saved scatter award differs from the visible FREE symbols.");
            if (result.SpecialPointsAwarded != MidnightVaultSpecialPoints.CalculateAward(evaluations, audit.EffectiveGame))
                Flag("slot-midnight-special-point-mismatch", "The saved special-point award differs from the visible POWER matches.");
            var seals = MidnightVaultEngine.CountSeals(result.Reels);
            if (!SameMap(result.SealsAwarded, seals))
                Flag("slot-midnight-seal-mismatch", "The saved gem awards differ from the visible collection symbols.");
            var full = MidnightVaultEngine.HasPayingFullMatch(evaluations, audit.EffectiveGame, audit.Paytable);
            var expectedMisses = full ? 0 : checked(audit.PityMissesBefore + 1);
            var expectedPity = string.IsNullOrWhiteSpace(audit.FreeSpinFeatureMode) &&
                audit.BaseGame.Math.FiveMatchPityMissLimit is { } limit && audit.PityMissesBefore >= limit;
            if (result.ConsecutiveFiveMisses != expectedMisses || result.FiveMatchPityTriggered != expectedPity ||
                expectedPity && !full || audit.GenerationAttempts < 1 ||
                (!expectedPity && audit.GenerationAttempts != 1))
                Flag("slot-midnight-pity-mismatch", "The saved pity transition differs from the evaluated outcome.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            Flag("slot-midnight-replay-invalid", "The captured Midnight Vault outcome cannot be replayed.");
        }
        return findings;
    }

    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    private static bool SameMap(IReadOnlyDictionary<string, int> left, IReadOnlyDictionary<string, int> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);
}
