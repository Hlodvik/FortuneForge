using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using FortuneForge.Server.Slots.Games.NeonNights;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;

namespace FortuneForge.Server.RoundAudit;

// Runs against the frozen rules used by the spin. Random draws cannot be reconstructed
// from a CSPRNG result, but the resulting reels, awards, and settlement can be checked.
internal static class SlotRoundAudit
{
    internal const string AuditorVersion = "slot-outcome-auditor-v1";

    internal static IReadOnlyList<RoundAuditFinding> Check(SpinResult result, SpinPayout settledPayout, bool isPaid = false)
    {
        if (result.EngineAudit is not { } audit)
            return [new("slot-engine-evidence-missing", "error",
                "The engine's rules and generated reel evidence were not captured.")];
        if (audit.EngineVersion == FortuneForge.Games.JadeDragon.JadeDragonEngine.Version)
            return Slots.Games.JadeDragon.JadeDragonAudit.Check(result, settledPayout);
        if (result.GameId == SlotSpecialRoundProfiles.JadeDragonGameId)
            return [new("slot-empty-version-mismatch", "error",
                "Jade Dragon evidence must use its owned engine version.")];
        if (audit.EngineVersion == FortuneForge.Games.MidnightVault.MidnightVaultEngine.Version)
            return Slots.Games.MidnightVault.MidnightVaultAudit.Check(result, settledPayout);
        if (audit.EngineVersion == FortuneForge.Games.GoldFoundry.GoldFoundryEngine.Version)
            return GoldFoundryAudit.Check(result, settledPayout);
        if (NeonArcadeSlotDefinitions.SupportsPackageReplay(audit.EngineVersion))
        {
            var findings = NeonOutcomeReplay.Check(
                FortuneForge.Server.Slots.Games.NeonNights.NeonNightsEngine.ToPackage(result, audit),
                settledPayout).Select(finding => new RoundAuditFinding(finding.Code, "error", finding.Message)).ToList();
            if (audit.IsDerivedFreeSpin || audit.WukongPawPrototype is not null ||
                audit.OlympusTumble is not null || result.OlympusTumble is not null ||
                result.LightningCollection is not null || result.MonkeyPawCount != 0 ||
                result.MoneyGrabPoints != 0 || result.SealsAwarded.Count != 0)
                findings.Add(new("slot-neon-state-mismatch", "error", "The Neon outcome includes another game's state."));
            return findings;
        }
        var usesOlympusTumble = OlympusTumbleDemo.SupportsEngineVersion(audit.EngineVersion);
        if ((audit.OlympusTumble is not null) != usesOlympusTumble ||
            (result.OlympusTumble is not null) != usesOlympusTumble)
            return [new("slot-olympus-tumble-version-mismatch", "error",
                "The tumble result and draw evidence do not match the captured engine version.")];
        if (usesOlympusTumble)
            return CheckOlympusTumble(result, audit, settledPayout, isPaid);
        if (GoldFoundryAudit.CheckModel(audit) is { } foundryFinding)
            return [foundryFinding];
        if ((audit.WukongPawPrototype is not null) !=
            (audit.EngineVersion == WukongPawPrototypeEvidence.EngineVersion))
            return [new("slot-wukong-prototype-version-mismatch", "error",
                "The preview collection rule is not bound to its captured engine version.")];
        var usesNeonSpecialPlays = NeonArcadeSlotDefinitions.SupportsSpecialPlays(audit.EngineVersion);
        var usesNeonEngine = usesNeonSpecialPlays || audit.EngineVersion == NeonArcadeSlotDefinitions.LegacyEngineVersion;
        if ((audit.NeonArcade is not null) != usesNeonEngine ||
            audit.NeonArcade is { } arcade &&
            arcade.FeatureGamesEnabled != usesNeonSpecialPlays)
            return [new("slot-neon-arcade-version-mismatch", "error",
                "The arcade payline rule is not bound to its captured engine version.")];
        if ((audit.NeonDiscoCascade is null) != (result.NeonDiscoCascade is null) ||
            audit.NeonDiscoCascade is not null &&
            !NeonArcadeSlotDefinitions.SupportsDiscoCascade(audit.EngineVersion))
            return [new("slot-neon-disco-version-mismatch", "error",
                "The Disco cascade does not match its captured engine version.")];
        if (RainbowRealmSlotDefinitions.IsModel(audit.BaseGame) !=
            (audit.EngineVersion == RainbowRealmSlotDefinitions.EngineVersion))
            return [new("slot-rainbow-model-mismatch", "error",
                "The charm harvest rule does not match the saved Rainbow Realm model.")];
        var usesArcaneCollector = audit.EngineVersion == ArcaneLightningCollector.EngineVersion;
        if (usesArcaneCollector != ArcaneLightningCollector.IsModel(audit.BaseGame) ||
            usesArcaneCollector && (!ArcaneLightningCollector.IsModel(audit.EffectiveGame) ||
                audit.BaseGame.Energy is not null || audit.EffectiveGame.Energy is not null ||
                audit.FeatureProfile?.UsesEnergy != false))
            return [new("slot-arcane-collector-version-mismatch", "error",
                "The lightning collector rule does not match the saved Arcane Archives model.")];
        return Check(result, audit.BaseGame, audit.EffectiveGame, audit.ReelSet,
            audit.SymbolSet, audit.Paytable, settledPayout, audit.EnergyBalanceBefore,
            audit.PityMissesBefore, audit.FreeSpinFeatureMode, audit.RawGeneratedReels,
            audit.ReelsAfterFreeGameFrequency, audit.IsDerivedFreeSpin, audit.WukongPawPrototype, audit.NeonArcade,
            isPaid: isPaid, neonEngineVersion: usesNeonEngine ? audit.EngineVersion : null);
    }

    private static IReadOnlyList<RoundAuditFinding> CheckOlympusTumble(
        SpinResult result, SlotSpinEngineAudit audit, SpinPayout settledPayout, bool isPaid)
    {
        if (result.GameId != SlotSpecialRoundProfiles.GodsOfOlympusGameId ||
            audit.BaseGame.Id != result.GameId || audit.EffectiveGame.Id != result.GameId ||
            result.ReelSetId != audit.EngineVersion ||
            result.PaytableId != audit.EngineVersion ||
            result.SymbolSetId != audit.SymbolSet.Id ||
            result.SymbolSetId != audit.BaseGame.Symbols.SymbolSetId ||
            result.PointValueInCents != audit.BaseGame.Wagering.PointValueInCents ||
            !ValidWager(result.WagerPoints, audit.BaseGame.Wagering))
            return [new("slot-olympus-tumble-rules-mismatch", "error",
                "The tumble game's identifiers or wager do not match the captured rules.")];

        var evidence = audit.OlympusTumble!;
        if (isPaid && (!OlympusTumbleDemo.SupportsPaidEngineVersion(audit.EngineVersion) || evidence.Showcase))
            return [new("slot-olympus-tumble-paid-mode-mismatch", "error",
                "A paid tumble requires rules supporting paid play and random draws.")];

        var validProgress = isPaid
            ? (result.SlotsCreditsBalance is null or >= 0m) && audit.PityMissesBefore >= 0 &&
                result.ConsecutiveFiveMisses == audit.PityMissesBefore && audit.EnergyBalanceBefore >= 0 &&
                HasNoOlympusTumbleProgression(result)
            : HasOnlyOlympusDemoState(result) && audit.PityMissesBefore == 0 && audit.EnergyBalanceBefore == 0;
        if (!validProgress ||
            audit.FreeSpinFeatureMode is not null || audit.IsDerivedFreeSpin)
            return [new("slot-olympus-tumble-progression-mismatch", "error",
                "The tumble outcome changes bonus progression or includes unexpected settlement state.")];

        var draws = evidence.DrawIndexes;
        var nextDraw = 0;
        int ReplayDraw(int upperBound)
        {
            if (draws is null || nextDraw >= draws.Count)
                throw new ArgumentException("The saved tumble draw sequence is incomplete.");
            var index = draws[nextDraw++];
            if (index < 0 || index >= upperBound)
                throw new ArgumentException("A saved tumble draw index is out of range.");
            return index;
        }

        try
        {
            var expected = OlympusTumbleDemo.Resolve(audit.EngineVersion, result.WagerPoints, evidence.Showcase, ReplayDraw);
            if (draws is null || nextDraw != draws.Count ||
                !SameOlympusReels(expected.InitialReels, result.Reels) ||
                !SameOlympusReels(expected.InitialReels, audit.RawGeneratedReels) ||
                !SameOlympusReels(expected.InitialReels, audit.ReelsAfterFreeGameFrequency) ||
                result.ReelStops.Count != expected.InitialReels.Count || result.ReelStops.Any(stop => stop != 0) ||
                JsonSerializer.Serialize(expected, RoundAuditCapture.Json) !=
                    JsonSerializer.Serialize(result.OlympusTumble, RoundAuditCapture.Json))
                return [new("slot-olympus-tumble-outcome-mismatch", "error",
                    "The saved boards, cleared symbols, or awards differ from the recorded draws.")];
            if (result.Payout.TotalPoints != expected.TotalPoints || settledPayout.TotalPoints != expected.TotalPoints ||
                result.Payout.Paylines.Count != 0 || settledPayout.Paylines.Count != 0)
                return [new("slot-olympus-tumble-payout-mismatch", "error",
                    "The saved payout differs from the replayed tumble total.")];
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            return [new("slot-olympus-tumble-draw-invalid", "error",
                "The saved draw sequence cannot reproduce this tumble outcome.")];
        }
        return [];
    }

    internal static bool HasOnlyOlympusDemoState(SpinResult result) =>
        result.SlotsCreditsBalance is null && result.ConsecutiveFiveMisses == 0 && HasNoOlympusTumbleProgression(result);

    private static bool HasNoOlympusTumbleProgression(SpinResult result) =>
        !result.IsFreeSpin && result.FreeSpinsAwarded == 0 && result.FreeSpinsRemaining == 0 &&
        result.FreeSpinWagerPoints is null && result.FreeSpinFeatureMode is null &&
        result.BonusRound is null && result.QueuedBonusRounds.Count == 0 && result.AwardedBonusRounds.Count == 0 &&
        result.MidnightBonusState is null && result.CollectionFreeSpinsAwarded == 0 &&
        result.SealsAwarded.Count == 0 && result.SealCollections.Count == 0 &&
        !result.FiveMatchPityTriggered && !result.SpecialBoostApplied &&
        result.SpecialPointsAwarded == 0 && result.SpecialPointsBalance == 0 &&
        result.EnergyAwarded == 0 && result.EnergyBalance == 0 && !result.EnergyMultiplierApplied &&
        result.PayoutMultiplier == 1m && result.NeonArcade is null && result.LightningCollection is null &&
        result.MonkeyPawCount == 0 && result.MoneyGrabPoints == 0 && result.BananaBonusPoints == 0 &&
        result.CrucibleWageredCents is null && result.CruciblePourValueCents is null &&
        result.CruciblePoured is null && result.CruciblePayoutCents is null;

    private static bool SameOlympusReels(
        IReadOnlyList<IReadOnlyList<string>> expected, IReadOnlyList<IReadOnlyList<string>> recorded) =>
        expected.Count == recorded.Count && expected.Zip(recorded).All(pair => pair.First.SequenceEqual(pair.Second));

    internal static IReadOnlyList<RoundAuditFinding> Check(
        SpinResult result,
        GameDefinition baseGame,
        GameDefinition effectiveGame,
        ReelSetDefinition reelSet,
        SymbolSetDefinition symbolSet,
        PaytableDefinition paytable,
        SpinPayout settledPayout,
        long preSpinEnergy,
        int preSpinPityMisses,
        string? activeFeatureMode,
        IReadOnlyList<IReadOnlyList<string>>? rawGeneratedReels = null,
        IReadOnlyList<IReadOnlyList<string>>? reelsAfterFrequency = null,
        bool isDerivedFreeSpin = false,
        WukongPawPrototypeEvidence? pawPrototype = null,
        NeonArcadeEvidence? neonArcade = null,
        bool isPaid = false,
        string? neonEngineVersion = null)
    {
        var findings = new List<RoundAuditFinding>();
        void Flag(string code, string message) => findings.Add(new(code, "error", message));
        var foundryCollector = GoldFoundrySlotDefinitions.IsModel(baseGame);
        var rainbowHarvest = RainbowRealmSlotDefinitions.IsModel(baseGame);
        var arcaneCollector = ArcaneLightningCollector.IsModel(baseGame);

        if (pawPrototype is not null &&
            (result.GameId != SlotSpecialRoundProfiles.WukongGameId || !pawPrototype.SyntheticOnly ||
             pawPrototype.CollectionRule != WukongPawPrototypeEvidence.RuleVersion ||
             string.IsNullOrWhiteSpace(pawPrototype.SessionId) || string.IsNullOrWhiteSpace(pawPrototype.AdmissionId) ||
             pawPrototype.WagerPoints != result.WagerPoints ||
             pawPrototype.ForcedPawColumn is { } column &&
                 (column < 0 || column >= result.Reels.Count || string.IsNullOrWhiteSpace(pawPrototype.GrantId) ||
                  result.Reels[column].Count == 0 || result.Reels[column][0] != "PAW")))
            Flag("slot-wukong-prototype-invalid", "The synthetic paw placement or collection rule is inconsistent.");

        if (result.GameId != baseGame.Id || result.GameId != effectiveGame.Id ||
            result.SymbolSetId != symbolSet.Id || result.ReelSetId != reelSet.Id ||
            result.PaytableId != paytable.Id ||
            reelSet.SymbolSetId != symbolSet.Id || paytable.SymbolSetId != symbolSet.Id)
            Flag("slot-rules-mismatch", "The spin identifiers do not match the captured rules.");
        var trustedBonusWager = isDerivedFreeSpin && result.IsFreeSpin &&
            (result.GameId == SlotSpecialRoundProfiles.DragonHoardGameId &&
                 MidnightVaultBonusPolicy.IsValidDerivedWager(result.WagerPoints) ||
             arcaneCollector && ArcaneLightningCollector.IsValidCarryWager(baseGame, result.WagerPoints));
        var legacyOlympusCarryWager = isPaid && result.IsFreeSpin &&
            result.GameId == SlotSpecialRoundProfiles.GodsOfOlympusGameId &&
            OlympusTumbleDemo.IsLegacyCarryWager(baseGame.Wagering, result.WagerPoints);
        var neonCarryWager = result.IsFreeSpin && NeonArcadeSlotDefinitions.IsModel(baseGame) &&
            NeonArcadeSlotDefinitions.IsValidCarryWager(result.WagerPoints);
        if (result.PointValueInCents != baseGame.Wagering.PointValueInCents ||
            !(trustedBonusWager || legacyOlympusCarryWager || neonCarryWager || ValidWager(result.WagerPoints, baseGame.Wagering)) ||
            isDerivedFreeSpin && !trustedBonusWager)
            Flag("slot-wager-invalid", "The wager or point value does not match the captured rules.");
        if (result.GameId == SlotSpecialRoundProfiles.DragonHoardGameId &&
            (baseGame.Math.PaytableId != paytable.Id ||
             (paytable.Id is MidnightVaultSlotDefinitions.PreviousPaytableId or MidnightVaultSlotDefinitions.PaytableId) &&
             baseGame.Math.PayoutMultiplier != 1m))
            Flag("slot-model-mismatch", "Midnight Vault's frozen payout model is inconsistent.");

        if (result.Reels.Count != effectiveGame.Layout.ReelCount ||
            result.Reels.Any(reel => reel.Count != effectiveGame.Layout.VisibleRows) ||
            result.ReelStops.Count != effectiveGame.Layout.ReelCount ||
            reelSet.Reels.Count != effectiveGame.Layout.ReelCount)
        {
            Flag("slot-grid-invalid", "The reel window, stop count, or reel set has an invalid shape.");
            return findings;
        }

        var validSymbols = symbolSet.Symbols.Select(symbol => symbol.Id).ToHashSet(StringComparer.Ordinal);
        if (result.Reels.SelectMany(reel => reel).Any(symbol => !validSymbols.Contains(symbol)))
            Flag("slot-symbol-invalid", "The visible window contains a symbol outside the captured symbol set.");
        if (pawPrototype is not null)
        {
            var earnPositions = result.Reels.SelectMany((reel, column) => reel.Select((symbol, row) =>
                (Symbol: symbol, Column: column, Row: row)))
                .Where(cell => cell.Symbol == WukongPawPrototypeEvidence.EarnSymbolId).ToArray();
            var validColumn = pawPrototype.ForcedPawColumn is { } forced && forced >= 0 && forced < result.Reels.Count
                ? forced : 0;
            var earnColumn = result.Reels.Count > 0 ? (validColumn + 1) % result.Reels.Count : 0;
            var validEarnMarker = pawPrototype.InjectEarnSymbol
                ? result.Reels.Count > 0 && earnPositions.Length == 1 &&
                    earnPositions[0].Column == earnColumn && earnPositions[0].Row == result.Reels[earnColumn].Count - 1
                : earnPositions.Length == 0;
            if (!validEarnMarker)
                Flag("slot-wukong-earn-marker-mismatch", "The illustrative earn symbol differs from the captured fixture placement.");
        }
        for (var reel = 0; reel < result.ReelStops.Count; reel++)
        {
            var playable = CryptoReelGenerator.GetPlayableStrip(effectiveGame, reelSet.Reels[reel]);
            var playableCount = playable.Count;
            if (result.ReelStops[reel] < 0 || result.ReelStops[reel] >= playableCount)
                Flag("slot-stop-invalid", $"Reel {reel + 1} has a stop outside its captured strip.");
            else if (rawGeneratedReels is not null && rawGeneratedReels.Count == result.Reels.Count &&
                rawGeneratedReels[reel].Count == effectiveGame.Layout.VisibleRows &&
                rawGeneratedReels[reel].Where((symbol, row) =>
                    symbol != playable[(result.ReelStops[reel] + row) % playableCount]).Any())
                Flag("slot-raw-reel-mismatch", $"The generated window for reel {reel + 1} does not match its stop and strip.");
        }
        if (rawGeneratedReels is not null &&
            (rawGeneratedReels.Count != result.Reels.Count ||
             rawGeneratedReels.Any(reel => reel.Count != effectiveGame.Layout.VisibleRows)))
            Flag("slot-raw-reel-mismatch", "The generated reel window has an invalid shape.");
        if (rawGeneratedReels is not null && reelsAfterFrequency is not null &&
            !ValidFrequencyStage(rawGeneratedReels, reelsAfterFrequency, effectiveGame))
            Flag("slot-frequency-stage-mismatch", "The scatter frequency stage changed an unexpected symbol.");
        if (foundryCollector && GoldFoundryAudit.CheckReelWindow(result, activeFeatureMode, rawGeneratedReels) is { } reelFinding)
            findings.Add(reelFinding);

        try
        {
            var evaluations = new CombinationEvaluator().Evaluate(result.Reels, effectiveGame, symbolSet);
            var payoutGame = NeonArcadeSlotDefinitions.UsesExpandedArcadeGrid(neonEngineVersion) ? effectiveGame : baseGame;
            var basePayout = new PayoutCalculator().Calculate(evaluations, payoutGame, paytable, result.WagerPoints);
            var neonFreeSpinsAwarded = 0;
            if (NeonArcadeSlotDefinitions.IsModel(baseGame))
            {
                var featuresEnabled = neonArcade?.FeatureGamesEnabled ?? result.NeonArcade?.FeatureGamesEnabled ?? false;
                var capturedVersion = neonEngineVersion ?? (featuresEnabled
                    ? NeonArcadeSlotDefinitions.BaseOnlyEngineVersion : NeonArcadeSlotDefinitions.LegacyEngineVersion);
                var expected = NeonArcadeBonus.Apply(basePayout, result.Reels, isFreeSpin: result.IsFreeSpin,
                    engineVersion: capturedVersion);
                if (baseGame.Wagering.PointValueInCents != 1m ||
                    JsonSerializer.Serialize(expected.Evidence) != JsonSerializer.Serialize(result.NeonArcade) ||
                    neonArcade is not null && JsonSerializer.Serialize(neonArcade) != JsonSerializer.Serialize(result.NeonArcade))
                    Flag("slot-neon-arcade-mismatch", "The arcade boost or feature symbols do not replay from the visible reels.");
                basePayout = expected.Payout;
                neonFreeSpinsAwarded = expected.FreeSpinsAwarded;
            }
            else if (result.NeonArcade is not null || neonArcade is not null)
                Flag("slot-neon-arcade-mismatch", "Arcade metadata is only valid for its isolated Neon model.");
            var recordedBaseLines = result.Payout.Paylines.Where(line => line.PaylineId < 801).ToArray();
            if (!LinesEqual(recordedBaseLines, basePayout.Paylines))
                Flag("slot-base-payout-mismatch", "The base payline breakdown does not replay from the visible reels.");

            var expectedFreeSpins = effectiveGame.FreeGames is { } freeGames &&
                result.Reels.SelectMany(reel => reel).Count(symbol => symbol == freeGames.SymbolId) >=
                freeGames.RequiredSymbols ? freeGames.AwardedSpins : neonFreeSpinsAwarded;
            if (result.FreeSpinsAwarded != expectedFreeSpins)
                Flag("slot-free-spins-mismatch", "The free-spin award does not match the visible symbols.");
            if (result.SpecialPointsAwarded != SpecialPointBonus.CalculateAward(evaluations, effectiveGame))
                Flag("slot-special-points-mismatch", "The special-point award does not match the visible symbols.");

            var energyAward = effectiveGame.Energy is { } energy
                ? result.Reels.SelectMany(reel => reel).Count(symbol => symbol == energy.SymbolId) *
                  energy.PointsPerVisibleSymbol : 0;
            if (result.EnergyAwarded != energyAward)
                Flag("slot-energy-award-mismatch", "The energy award does not match the visible symbols.");
            var seals = (foundryCollector || NeonArcadeSlotDefinitions.IsModel(baseGame)
                    ? Enumerable.Empty<string>() : result.Reels.SelectMany(reel => reel))
                .Where(symbol => symbol is "SEAL_SYNC" or "SEAL_ROWS" or "SEAL_PAW" or "SEAL_RAND")
                .GroupBy(symbol => symbol, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            if (seals.Count != result.SealsAwarded.Count ||
                seals.Any(pair => !result.SealsAwarded.TryGetValue(pair.Key, out var count) || count != pair.Value))
                Flag("slot-seals-mismatch", "The collection symbol counts do not match the visible reels.");

            var fullMatch = evaluations.Any(evaluation => evaluation.Candidates.Any(candidate =>
                candidate.Matches.Any(match => match.MatchLength == effectiveGame.Layout.ReelCount &&
                    paytable.Rules.Any(rule => rule.SymbolId == match.SymbolId &&
                        rule.MatchLength == match.MatchLength && rule.Multiplier > 0))));
            var expectedPity = string.IsNullOrWhiteSpace(activeFeatureMode) &&
                baseGame.Math.FiveMatchPityMissLimit is { } limit && preSpinPityMisses >= limit;
            if (result.FiveMatchPityTriggered != expectedPity ||
                result.ConsecutiveFiveMisses != (fullMatch ? 0 : checked(preSpinPityMisses + 1)) ||
                (expectedPity && !fullMatch))
                Flag("slot-pity-state-mismatch", "The pity state transition does not match the spin outcome.");

            var pawCount = result.Reels.SelectMany(reel => reel).Count(symbol => symbol == "PAW");
            var moneyMultiplier = result.Reels.SelectMany(reel => reel)
                .Sum(symbol => foundryCollector ? GoldFoundryReelCollector.IngotMultiplier(symbol) :
                    rainbowHarvest ? RainbowRealmCharmHarvest.WagerMultiplier(symbol) : RandMultiplier(symbol));
            var collectedPoints = pawCount > 0 && moneyMultiplier > 0
                ? RoundPoints(result.WagerPoints * moneyMultiplier *
                    (pawPrototype is not null ? pawCount : Math.Min(pawCount, 2))) : 0;
            var moneyPoints = pawPrototype is not null || foundryCollector || rainbowHarvest
                ? collectedPoints : Scale(collectedPoints, baseGame.Math.PayoutMultiplier);
            var bananaPatterns = CountBananaPatterns(result.Reels);
            var bananaMultiplier = NeonArcadeSlotDefinitions.IsModel(baseGame)
                ? NeonArcadeSlotDefinitions.TripleSevenMultiplierForEngine(neonEngineVersion) : 3;
            var bananaUnit = Scale(checked(result.WagerPoints * bananaMultiplier),
                foundryCollector || NeonArcadeSlotDefinitions.IsModel(baseGame)
                    ? 1m : baseGame.Math.PayoutMultiplier);
            var bananaPoints = checked(bananaPatterns * bananaUnit);
            if (result.MonkeyPawCount != pawCount || result.MoneyGrabPoints != moneyPoints ||
                result.BananaBonusPoints != bananaPoints)
                Flag("slot-feature-award-mismatch", "The feature awards do not match the visible reels.");
            var moneyLines = result.Payout.Paylines.Where(line => line.PaylineId == 901).ToArray();
            var bananaLines = result.Payout.Paylines.Where(line => line.PaylineId is >= 801 and < 901).ToArray();
            var lightningLines = result.Payout.Paylines
                .Where(line => line.PaylineId == ArcaneLightningCollector.PaylineId).ToArray();
            var expectedCollection = arcaneCollector
                ? ArcaneLightningCollector.Calculate(result.Reels, result.WagerPoints) : null;
            var lightningPoints = expectedCollection?.TotalPoints ?? 0;
            IReadOnlyList<PaylinePayout> expectedLightningLines = expectedCollection is null
                ? [] : [ArcaneLightningCollector.CreatePayout(expectedCollection, result.WagerPoints)];
            if (!SameLightningCollection(expectedCollection, result.LightningCollection) ||
                !LinesEqual(lightningLines, expectedLightningLines))
                Flag("slot-arcane-collector-mismatch",
                    "The lightning collector positions, values, or payout do not match the visible reels.");
            var offPaylinePoints = foundryCollector || rainbowHarvest ? moneyPoints : 0;
            var listedMoneyPoints = moneyPoints - offPaylinePoints;
            if (moneyLines.Length != (listedMoneyPoints > 0 ? 1 : 0) ||
                moneyLines.Sum(line => line.AmountPoints) != listedMoneyPoints ||
                bananaLines.Length != bananaPatterns ||
                bananaLines.Sum(line => line.AmountPoints) != bananaPoints ||
                bananaLines.Any(line => line.AmountPoints != bananaUnit) ||
                bananaLines.Any(line => line.Matches.Count != 1 ||
                    line.Matches[0].Multiplier != bananaMultiplier ||
                    line.Matches[0].AmountPoints != bananaUnit) ||
                !bananaLines.Select(line => line.PaylineId)
                    .SequenceEqual(Enumerable.Range(801, bananaLines.Length)) ||
                result.Payout.Paylines.Any(line => line.PaylineId >= 901 &&
                    line.PaylineId != 901 && line.PaylineId != ArcaneLightningCollector.PaylineId))
                Flag("slot-feature-payout-mismatch", "The feature payline breakdown does not match its award.");
            var expectedTotal = checked(basePayout.TotalPoints + moneyPoints + bananaPoints + lightningPoints);
            if (result.Payout.TotalPoints != expectedTotal ||
                result.Payout.TotalPoints != checked(result.Payout.Paylines.Sum(line => line.AmountPoints) + offPaylinePoints) ||
                result.Payout.Paylines.Any(line => line.AmountPoints != line.Matches.Sum(match => match.AmountPoints)))
                Flag("slot-payout-total-mismatch", "The outcome payout does not balance with its paylines and separate awards.");

            var fullEnergy = Math.Min(100, checked(Math.Clamp(preSpinEnergy, 0, 100) + energyAward));
            var usesEnergy = !arcaneCollector && !NeonArcadeSlotDefinitions.IsModel(baseGame) &&
                (result.GameId != SlotSpecialRoundProfiles.RainbowRealmGameId || effectiveGame.Energy is not null);
            var multiply = usesEnergy && !foundryCollector &&
                fullEnergy >= 100 && result.Payout.TotalPoints > 0;
            var expectedSettledLines = multiply
                ? result.Payout.Paylines.Select(line => line with
                {
                    Matches = line.Matches.Select(match => match with
                    {
                        AmountPoints = Scale(match.AmountPoints, 1.5m)
                    }).ToArray(),
                    AmountPoints = line.Matches.Sum(match => Scale(match.AmountPoints, 1.5m))
                }).ToArray()
                : result.Payout.Paylines;
            if (!LinesEqual(settledPayout.Paylines, expectedSettledLines) ||
                settledPayout.TotalPoints != checked(expectedSettledLines.Sum(line => line.AmountPoints) + offPaylinePoints))
                Flag("slot-settlement-mismatch", "The settled payout does not match the energy multiplier rules.");
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or
            InvalidOperationException or IndexOutOfRangeException or KeyNotFoundException)
        {
            Flag("slot-replay-failed", "The captured spin could not be replayed under its captured rules.");
        }

        return findings;
    }

    private static bool ValidWager(long wager, GameWageringDefinition rules) =>
        wager >= rules.MinimumWagerPoints && wager > 0 &&
        (rules.MaximumWagerPoints is null || wager <= rules.MaximumWagerPoints) &&
        (rules.AllowedWagerPoints.Count > 0
            ? rules.AllowedWagerPoints.Contains(wager)
            : rules.WagerIncrementPoints is { } increment && increment > 0 &&
              (wager - rules.MinimumWagerPoints) % increment == 0);

    private static bool ValidFrequencyStage(
        IReadOnlyList<IReadOnlyList<string>> raw,
        IReadOnlyList<IReadOnlyList<string>> after,
        GameDefinition game)
    {
        if (raw.Count != after.Count) return false;
        for (var reel = 0; reel < raw.Count; reel++)
        {
            if (raw[reel].Count != after[reel].Count) return false;
            for (var row = 0; row < raw[reel].Count; row++)
            {
                if (raw[reel][row] == after[reel][row]) continue;
                if (game.FreeGames is not { VisibleFrequencyDivisor: > 1 } freeGames ||
                    raw[reel][row] != freeGames.SymbolId ||
                    game.SpecialPoints?.CommonSymbolIds.Contains(after[reel][row]) != true)
                    return false;
            }
        }
        return true;
    }

    private static decimal RandMultiplier(string symbol) => symbol switch
    {
        "RAND_05" => .5m, "RAND_1" => 1m, "RAND_15" => 1.5m,
        "RAND_2" => 2m, "RAND_3" => 3m, "RAND_4" => 4m, "RAND_5" => 5m, _ => 0m
    };

    private static long RoundPoints(decimal points) =>
        checked((long)Math.Round(points, MidpointRounding.AwayFromZero));

    internal static bool SameLightningCollection(ArcaneLightningCollection? left, ArcaneLightningCollection? right) =>
        left is null ? right is null : right is not null &&
            left.Collector == right.Collector && left.TotalPoints == right.TotalPoints &&
            left.Cells is not null && right.Cells is not null && left.Cells.SequenceEqual(right.Cells);

    private static long Scale(long points, decimal multiplier) =>
        RoundPoints(points * multiplier);

    private static int CountBananaPatterns(IReadOnlyList<IReadOnlyList<string>> reels)
    {
        var patterns = new HashSet<string>(StringComparer.Ordinal);
        void Include(params (int Reel, int Row)[] cells)
        {
            if (cells.All(cell => cell.Reel >= 0 && cell.Reel < reels.Count &&
                cell.Row >= 0 && cell.Row < reels[cell.Reel].Count &&
                reels[cell.Reel][cell.Row] == "BANANA"))
                patterns.Add(string.Join('|', cells.OrderBy(cell => cell.Reel).ThenBy(cell => cell.Row)
                    .Select(cell => $"{cell.Reel}.{cell.Row}")));
        }
        var rows = reels.Max(reel => reel.Count);
        for (var reel = 0; reel < reels.Count; reel++)
            for (var row = 0; row <= reels[reel].Count - 3; row++)
                Include((reel, row), (reel, row + 1), (reel, row + 2));
        for (var row = 0; row < rows; row++)
            for (var reel = 0; reel <= reels.Count - 3; reel++)
                Include((reel, row), (reel + 1, row), (reel + 2, row));
        for (var reel = 0; reel <= reels.Count - 3; reel++)
            for (var row = 0; row <= rows - 3; row++)
                Include((reel, row), (reel + 1, row + 1), (reel + 2, row + 2));
        for (var reel = 0; reel <= reels.Count - 3; reel++)
            for (var row = 2; row < rows; row++)
                Include((reel, row), (reel + 1, row - 1), (reel + 2, row - 2));
        return patterns.Count;
    }

    private static bool LinesEqual(IReadOnlyList<PaylinePayout> actual, IReadOnlyList<PaylinePayout> expected)
    {
        if (actual.Count != expected.Count) return false;
        for (var index = 0; index < actual.Count; index++)
        {
            var left = actual[index];
            var right = expected[index];
            if (left.PaylineId != right.PaylineId || left.AmountPoints != right.AmountPoints ||
                left.Matches.Count != right.Matches.Count) return false;
            for (var matchIndex = 0; matchIndex < left.Matches.Count; matchIndex++)
            {
                var a = left.Matches[matchIndex];
                var b = right.Matches[matchIndex];
                if (a.AmountPoints != b.AmountPoints || a.Multiplier != b.Multiplier ||
                    a.Match.PaylineId != b.Match.PaylineId || a.Match.SymbolId != b.Match.SymbolId ||
                    a.Match.MatchLength != b.Match.MatchLength ||
                    !a.Match.Positions.SequenceEqual(b.Match.Positions) ||
                    !a.Match.WildPositions.SequenceEqual(b.Match.WildPositions)) return false;
            }
        }
        return true;
    }
}
