using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Controllers;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using FortuneForge.Server.Accounts.Models;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.RoundAudit;

public sealed class SlotRoundAuditTests
{
    [Fact]
    public void EveryCataloguedSlot_ValidSpinReplaysWithoutFindings()
    {
        var provider = Provider();
        Assert.Equal(20, SlotSpecialRoundProfiles.All.Count);
        foreach (var profile in SlotSpecialRoundProfiles.All)
        {
            var (result, game, reelSet, symbolSet, paytable) = Spin(provider, profile.GameId);
            var settled = EnergyBonus.Settle(0, result.EnergyAwarded, result.Payout).Payout;
            var findings = SlotRoundAudit.Check(result, settled);
            Assert.True(findings.Count == 0,
                $"{profile.GameId}: {string.Join(", ", findings.Select(finding => finding.Code))}");
        }
    }

    [Fact]
    public void RainbowRealm_StaleSunshineDoesNotChangeTheReelsOrPayout()
    {
        var provider = Provider();
        var gameId = SlotSpecialRoundProfiles.RainbowRealmGameId;
        var (ordinary, _, _, _, _) = Spin(provider, gameId);
        var (stale, _, reelSet, _, _) = Spin(provider, gameId, energy: 100);

        Assert.Equal(0, stale.EnergyAwarded);
        Assert.DoesNotContain("BOLT", reelSet.Reels.SelectMany(reel => reel));
        Assert.DoesNotContain("BOLT", stale.Reels.SelectMany(reel => reel));
        Assert.Equal(ordinary.Payout.TotalPoints, stale.Payout.TotalPoints);
        Assert.Equal(ordinary.Reels.SelectMany(reel => reel), stale.Reels.SelectMany(reel => reel));
        Assert.Empty(SlotRoundAudit.Check(stale, stale.Payout));
    }

    [Fact]
    public void MutatedPayoutAndReel_AreFlagged()
    {
        var provider = Provider();
        var (result, game, reelSet, symbolSet, paytable) = Spin(provider, SlotSpecialRoundProfiles.WukongGameId);
        var payout = result.Payout with { TotalPoints = result.Payout.TotalPoints + 1 };
        var payoutFindings = SlotRoundAudit.Check(result with { Payout = payout }, game, game,
            reelSet, symbolSet, paytable, payout, 0, 0, null);
        Assert.Contains(payoutFindings, finding => finding.Code == "slot-payout-total-mismatch");

        var changed = result.Reels.Select(reel => (IReadOnlyList<string>)reel.ToArray()).ToArray();
        changed[0] = changed[0].Select((symbol, row) => row == 0 ? "FORGED" : symbol).ToArray();
        var reelFindings = SlotRoundAudit.Check(result with { Reels = changed }, game, game,
            reelSet, symbolSet, paytable, result.Payout, 0, 0, null);
        Assert.Contains(reelFindings, finding => finding.Code == "slot-symbol-invalid");
    }

    [Fact]
    public void AlteredStopAndSettlement_AreFlagged()
    {
        var provider = Provider();
        var (result, game, reelSet, symbolSet, paytable) = Spin(provider, SlotSpecialRoundProfiles.PiratesFortuneGameId);
        var stops = result.ReelStops.ToArray();
        stops[0] = reelSet.Reels[0].Count + 1;
        var settlement = result.Payout with { TotalPoints = result.Payout.TotalPoints + 1 };
        var findings = SlotRoundAudit.Check(result with { ReelStops = stops }, game, game,
            reelSet, symbolSet, paytable, settlement, 0, 0, null);
        Assert.Contains(findings, finding => finding.Code == "slot-stop-invalid");
        Assert.Contains(findings, finding => finding.Code == "slot-settlement-mismatch");
    }

    [Fact]
    public void TamperedRawWindow_IsFlaggedAgainstCapturedStripAndStop()
    {
        var provider = Provider();
        var (result, _, _, _, _) = Spin(provider, SlotSpecialRoundProfiles.WukongGameId);
        var audit = Assert.IsType<SlotSpinEngineAudit>(result.EngineAudit);
        var raw = audit.RawGeneratedReels.Select(reel => (IReadOnlyList<string>)reel.ToArray()).ToArray();
        raw[0] = raw[0].Select((symbol, row) => row == 0 ? "FORGED" : symbol).ToArray();
        var changed = result with { EngineAudit = audit with { RawGeneratedReels = raw } };
        var findings = SlotRoundAudit.Check(changed, result.Payout);
        Assert.Contains(findings, finding => finding.Code == "slot-raw-reel-mismatch");
    }

    [Fact]
    public void FrozenSavedEvidence_ReplaysEveryCataloguedSlotWithoutCurrentOptions()
    {
        var provider = Provider();
        foreach (var profile in SlotSpecialRoundProfiles.All)
        {
            var (result, _, _, _, _) = Spin(provider, profile.GameId);
            var saved = SavedEvidence(result);
            var findings = SlotEvidenceReplayAudit.Inspect(saved.Summary, saved.PayloadJson);
            Assert.True(findings.Count == 0,
                $"{profile.GameId}: {string.Join(", ", findings.Select(finding => finding.Code))}");
        }
    }

    [Fact]
    public void TamperedSavedSettlement_IsFlaggedOnSecondPass()
    {
        var (result, _, _, _, _) = Spin(Provider(), SlotSpecialRoundProfiles.PiratesFortuneGameId);
        var saved = SavedEvidence(result);
        var tampered = JsonNode.Parse(saved.PayloadJson)!;
        var payout = tampered["data"]!["settledPayout"]!;
        payout["totalPoints"] = payout["totalPoints"]!.GetValue<long>() + 1;

        var findings = SlotEvidenceReplayAudit.Inspect(saved.Summary, tampered.ToJsonString());
        Assert.Contains(findings, finding => finding.Code == "slot-settlement-mismatch" && finding.Severity == "error");
    }

    [Fact]
    public void OlympusPaidTumble_ReplaysDrawsAndBindsTheAdmissionAndSettlement()
    {
        var random = new SeededRandom(72);
        var service = new SpinService(Provider(), new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random);
        var admission = new SlotSpinAdmission(2, 50, false, 0, false, 0, null, 23, null)
        {
            AdmissionId = Guid.NewGuid(),
            StartedAtUtc = DateTime.UtcNow,
            PityMissesBefore = 4,
            MathModelId = OlympusTumbleDemo.EngineVersion
        };
        var result = service.SpinAdmitted(SlotSpecialRoundProfiles.GodsOfOlympusGameId, admission, "audit-test-player");
        var audit = Assert.IsType<SlotSpinEngineAudit>(result.EngineAudit);
        var settledPayout = result.Payout;
        Assert.Equal(admission.PityMissesBefore, result.ConsecutiveFiveMisses);
        Assert.Equal(admission.EnergyBalance, audit.EnergyBalanceBefore);
        Assert.Empty(SlotRoundAudit.Check(result, settledPayout, isPaid: true));

        var roundId = result.SpinId.ToString("N");
        var saved = SinglePlayerRoundRecord.Create("gods-of-olympus", roundId, "audit-test-player",
            admission.ChargedWagerCents, RandMoney.PointsToCents(settledPayout.TotalPoints, result.PointValueInCents),
            admission.StartedAtUtc, DateTime.UtcNow, "slotSpinResults", roundId,
            new
            {
                result, engineAudit = audit, settledPayout, admission.IsFreeSpin,
                activeFreeSpinFeatureMode = admission.FreeSpinFeatureMode,
                admittedMathModelId = admission.MathModelId,
                payoutMultiplier = 1m
            },
            "slot-reels-paylines-and-features-v1", true, SlotRoundAudit.AuditorVersion).Audit;
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(saved.Summary, saved.PayloadJson));

        var changed = JsonNode.Parse(saved.PayloadJson)!;
        changed["wagerCents"] = admission.ChargedWagerCents + 1;
        Assert.Contains(SlotEvidenceReplayAudit.Inspect(saved.Summary, changed.ToJsonString()),
            finding => finding.Code == "slot-olympus-tumble-financial-mismatch");

        changed["wagerCents"] = admission.ChargedWagerCents;
        changed["data"]!["admittedMathModelId"] = OlympusTumbleDemo.LegacyEngineVersion;
        Assert.Contains(SlotEvidenceReplayAudit.Inspect(saved.Summary, changed.ToJsonString()),
            finding => finding.Code == "slot-olympus-tumble-admission-mismatch");

        changed["data"]!["admittedMathModelId"] = admission.MathModelId;
        changed["data"]!["engineAudit"]!["olympusTumble"]!["showcase"] = true;
        Assert.Contains(SlotEvidenceReplayAudit.Inspect(saved.Summary, changed.ToJsonString()),
            finding => finding.Code == "slot-olympus-tumble-paid-mode-mismatch");
    }

    [Fact]
    public void OlympusLegacyFreeSpin_ReplaysTheLockedOddWager()
    {
        var random = new SeededRandom(72);
        var service = new SpinService(Provider(), new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random);
        var admission = new SlotSpinAdmission(3, 0, true, 0, false, 0, null, 0, null)
        {
            AdmissionId = Guid.NewGuid(),
            StartedAtUtc = DateTime.UtcNow
        };
        var result = service.SpinAdmitted(SlotSpecialRoundProfiles.GodsOfOlympusGameId, admission, "audit-test-player");
        Assert.True(result.IsFreeSpin);
        Assert.Null(result.OlympusTumble);
        Assert.False(Assert.IsType<SlotSpinEngineAudit>(result.EngineAudit).IsDerivedFreeSpin);
        var saved = SavedEvidence(result);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(saved.Summary, saved.PayloadJson));
        Assert.Contains(SlotRoundAudit.Check(result, result.Payout), finding => finding.Code == "slot-wager-invalid");
    }

    [Fact]
    public void ArcaneLightning_ReplaysSavedCollectionAndRejectsChangedValuesAndSettlement()
    {
        var result = SpinArcane();
        var collection = Assert.IsType<ArcaneLightningCollection>(result.LightningCollection);
        Assert.Equal(8, collection.TotalPoints);
        var paid = SavedEvidence(result);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(paid.Summary, paid.PayloadJson));

        var changed = JsonNode.Parse(paid.PayloadJson)!;
        changed["data"]!["result"]!["lightningCollection"]!["cells"]![0]!["amountPoints"] = 5;
        Assert.Contains(SlotEvidenceReplayAudit.Inspect(paid.Summary, changed.ToJsonString()),
            finding => finding.Code == "slot-arcane-collector-mismatch");

        var lightningLine = Assert.Single(result.Payout.Paylines,
            line => line.PaylineId == ArcaneLightningCollector.PaylineId);
        var duplicatedPayout = result.Payout with { Paylines = [.. result.Payout.Paylines, lightningLine] };
        Assert.Contains(SlotRoundAudit.Check(result with { Payout = duplicatedPayout }, result.Payout),
            finding => finding.Code == "slot-arcane-collector-mismatch");

        var boosted = EnergyBonus.Settle(100, result.EnergyAwarded, result.Payout).Payout;
        Assert.Contains(SlotRoundAudit.Check(SpinArcane(energy: 100), boosted),
            finding => finding.Code == "slot-settlement-mismatch");

        var participant = Assert.Single(paid.Summary.Participants);
        var alteredSummary = paid.Summary with
        {
            Participants = [participant with { PayoutCents = participant.PayoutCents + 1 }]
        };
        Assert.Contains(SlotEvidenceReplayAudit.Inspect(alteredSummary, paid.PayloadJson),
            finding => finding.Code == "slot-arcane-financial-mismatch");

        var demo = SavedDemoEvidence(result);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(demo.Summary, demo.PayloadJson));
        var changedDemo = JsonNode.Parse(demo.PayloadJson)!;
        changedDemo["responseResult"]!["lightningCollection"]!["collector"]!["row"] = 1;
        Assert.Contains(SlotEvidenceReplayAudit.Inspect(demo.Summary, changedDemo.ToJsonString()),
            finding => finding.Code == "slot-demo-response-mismatch");
    }

    [Fact]
    public void ArcaneLightning_HistoricalEnergySpinStillReplays()
    {
        var result = SpinArcane(legacy: true);
        Assert.Equal(SpinService.AuditEngineVersion, result.EngineAudit?.EngineVersion);
        Assert.Equal(2, result.EnergyAwarded);
        Assert.Null(result.LightningCollection);
        var saved = SavedEvidence(result);
        Assert.Empty(SlotEvidenceReplayAudit.Inspect(saved.Summary, saved.PayloadJson));
    }

    [Fact]
    public void SavedDemoEvidence_ReplaysFrozenOutcomeAndNonfinancialProgression()
    {
        var (result, _, _, _, _) = Spin(Provider(), SlotSpecialRoundProfiles.WukongGameId);
        var saved = SavedDemoEvidence(result);

        var findings = SlotEvidenceReplayAudit.Inspect(saved.Summary, saved.PayloadJson);

        Assert.Empty(findings);
        Assert.Equal("demo:" + result.SpinId.ToString("N"), saved.Summary.RoundId);
    }

    [Fact]
    public void TamperedDemoResponseAndIdentity_AreFlaggedOnSecondPass()
    {
        var (result, _, _, _, _) = Spin(Provider(), SlotSpecialRoundProfiles.WukongGameId);
        var saved = SavedDemoEvidence(result);
        var tampered = JsonNode.Parse(saved.PayloadJson)!;
        tampered["responseResult"]!["freeSpinsRemaining"] = 999;

        var findings = SlotEvidenceReplayAudit.Inspect(saved.Summary, tampered.ToJsonString());
        Assert.Contains(findings, finding => finding.Code == "slot-demo-progression-mismatch" &&
            finding.Severity == "error");
        var identityFindings = SlotEvidenceReplayAudit.Inspect(
            saved.Summary with { RoundId = "demo:other" }, saved.PayloadJson);
        Assert.Contains(identityFindings, finding => finding.Code == "slot-evidence-identity-mismatch");
    }

    private static FortuneForge.Games.Abstractions.Auditing.RoundAuditCapture SavedDemoEvidence(SpinResult result)
    {
        var request = new DemoSpinRequest(result.GameId, result.WagerPoints,
            UseFreeSpin: false, FreeSpinsRemaining: 0, FreeSpinWagerPoints: null,
            EnergyBalance: 0, SealCollections: []);
        var energy = EnergyBonus.Settle(0, result.EnergyAwarded, result.Payout);
        SlotSpecialRoundProfiles.TryGet(result.GameId, out var profile);
        var specialRounds = profile is null
            ? new SlotSpecialRoundProgress(0, null, [])
            : SlotSpecialRoundProfiles.SettleDemo(profile, [], result.SealsAwarded,
                result.WagerPoints, energy.MultiplierApplied, result.SpinId);
        var freeSpinsRemaining = result.FreeSpinsAwarded + specialRounds.FreeSpinsAwarded;
        var piratesCollectionSpins = result.GameId == SlotSpecialRoundProfiles.PiratesFortuneGameId
            ? specialRounds.FreeSpinsAwarded : 0;
        var response = result with
        {
            SlotsCreditsBalance = null,
            Payout = energy.Payout,
            FreeSpinsAwarded = checked(result.FreeSpinsAwarded + piratesCollectionSpins),
            CollectionFreeSpinsAwarded = piratesCollectionSpins,
            FreeSpinsRemaining = freeSpinsRemaining,
            FreeSpinWagerPoints = freeSpinsRemaining > 0 ? result.WagerPoints : null,
            EnergyBalance = energy.FinalEnergyBalance,
            EnergyMultiplierApplied = energy.MultiplierApplied,
            PayoutMultiplier = energy.PayoutMultiplier,
            SealCollections = specialRounds.Collections,
            FreeSpinFeatureMode = freeSpinsRemaining > 0
                ? specialRounds.FeatureMode ??
                    (result.FreeSpinsAwarded > 0 ? profile?.ScatterFeatureMode : null)
                : null
        };
        var generated = Assert.IsType<SlotSpinEngineAudit>(result.EngineAudit).GeneratedAtUtc;
        return DemoSlotRoundRecording.Capture(new DemoSlotAuditEntry(request, result,
            response, generated.AddMilliseconds(-1), generated.AddMilliseconds(1)));
    }

    private static FortuneForge.Games.Abstractions.Auditing.RoundAuditCapture SavedEvidence(SpinResult result)
    {
        var audit = Assert.IsType<SlotSpinEngineAudit>(result.EngineAudit);
        var wagerCents = result.IsFreeSpin ? 0 : RandMoney.PointsToCents(result.WagerPoints, result.PointValueInCents);
        const long wageredBeforeCents = 0;
        var crucible = GoldFoundryCrucible.IsGame(result.GameId) && audit.CrucibleRuleVersion is { } ruleVersion
            ? GoldFoundryCrucible.Settle(audit.EnergyBalanceBefore, result.EnergyAwarded,
                wageredBeforeCents, wagerCents, ruleVersion)
            : null;
        var settledPayout = crucible?.AsEnergySettlement(result.Payout).Payout
            ?? EnergyBonus.Settle(0, result.EnergyAwarded, result.Payout).Payout;
        var catalogueGameId = FortuneForge.Server.Activity.AcceptedGameActivityReader.SlotIds[result.GameId];
        var piratesGemEvidence = result.GameId == SlotSpecialRoundProfiles.PiratesFortuneGameId
            ? PiratesGemEvidence(result) : null;
        var payoutCents = RandMoney.PointsToCents(settledPayout.TotalPoints, result.PointValueInCents)
            + (crucible?.PayoutCents ?? 0);
        return SinglePlayerRoundRecord.Create(catalogueGameId, result.SpinId.ToString("N"),
            "audit-test-player", wagerCents, payoutCents, DateTime.UtcNow, DateTime.UtcNow,
            "slotSpinResults", result.SpinId.ToString("N"),
            new
            {
                result, engineAudit = audit, settledPayout, result.IsFreeSpin, piratesGemEvidence,
                crucible = crucible is null ? null : new { wageredBeforeCents, settlement = crucible }
            },
            "slot-reels-paylines-and-features-v1", true, SlotRoundAudit.AuditorVersion).Audit;
    }

    private static PiratesGemSettlementEvidence PiratesGemEvidence(SpinResult result)
    {
        var beforeCounts = PiratesGemBonus.Modes.ToDictionary(mode => mode, _ => 0L);
        var beforeWagers = PiratesGemBonus.Modes.ToDictionary(mode => mode, _ => 0L);
        var afterCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var afterWagers = new Dictionary<string, long>(StringComparer.Ordinal);
        var spinWagerCents = RandMoney.PointsToCents(result.WagerPoints, result.PointValueInCents);
        var awards = 0;
        string? featureMode = null;
        long freeSpinWagerPoints = 0;
        foreach (var mode in PiratesGemBonus.Modes)
        {
            var awarded = result.SealsAwarded
                .Where(pair => PiratesGemBonus.ModesBySymbol.TryGetValue(pair.Key, out var matched) && matched == mode)
                .Sum(pair => pair.Value);
            var progress = PiratesGemBonus.Advance(result.SpinId, mode, 0, 0, awarded, spinWagerCents);
            afterCounts[mode] = progress.Count;
            afterWagers[mode] = progress.WagerTotal;
            awards += progress.Awards.Count;
            if (progress.Awards.Count > 0)
                featureMode = PiratesGemBonus.AddFeatureMode(featureMode, mode);
            if (freeSpinWagerPoints == 0 && progress.Awards.Count > 0)
                freeSpinWagerPoints = result.WagerPoints;
        }
        var profile = result.EngineAudit!.FeatureProfile!;
        return new PiratesGemSettlementEvidence(PiratesGemBonus.RuleVersion,
            beforeCounts, beforeWagers, afterCounts, afterWagers,
            awards * profile.CollectionAwardedSpins,
            featureMode,
            freeSpinWagerPoints);
    }

    private static (SpinResult Result, GameDefinition Game, ReelSetDefinition ReelSet,
        SymbolSetDefinition Symbols, PaytableDefinition Paytable) Spin(
        OptionsSlotsDefinitionProvider provider, string gameId, long energy = 0)
    {
        var game = provider.GetGame(gameId)!;
        var reelSet = provider.GetReelSet(game.Math.ReelSetId)!;
        var symbolSet = provider.GetSymbolSet(game.Symbols.SymbolSetId)!;
        var paytable = provider.GetPaytable(game.Math.PaytableId)!;
        var random = new SeededRandom(72);
        var service = new SpinService(provider, new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random);
        var wager = game.Wagering.AllowedWagerPoints.Count > 0
            ? game.Wagering.AllowedWagerPoints[0] : game.Wagering.MinimumWagerPoints;
        var result = service.Spin(gameId, wager, "audit-test-player", false, currentEnergyBalance: energy);
        return (result, game, reelSet, symbolSet, paytable);
    }

    private static OptionsSlotsDefinitionProvider Provider()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        var options = document.RootElement.GetProperty("Slots")
            .Deserialize<SlotsOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return new OptionsSlotsDefinitionProvider(Options.Create(options));
    }

    private static SpinResult SpinArcane(bool legacy = false, long energy = 0)
    {
        var random = new ArcaneFixtureRandom();
        var service = new SpinService(new ArcaneFixtureProvider(Provider(), legacy),
            new CryptoReelGenerator(random), new CombinationEvaluator(), new PayoutCalculator(), random);
        var result = service.Spin(SlotSpecialRoundProfiles.ArcaneArchivesGameId, 2, "arcane-audit-test",
            false, currentEnergyBalance: energy);
        return legacy ? result with
        {
            EngineAudit = result.EngineAudit! with
            {
                FeatureProfile = result.EngineAudit.FeatureProfile! with { UsesEnergy = true }
            }
        } : result;
    }

    private sealed class ArcaneFixtureProvider(OptionsSlotsDefinitionProvider provider, bool legacy)
        : ISlotsDefinitionProvider
    {
        public GameDefinition GetGame(string id)
        {
            var current = provider.GetGame(id)!;
            if (!legacy) return current;
            return new GameDefinition
            {
                Id = current.Id, Layout = current.Layout, Symbols = current.Symbols,
                Matching = current.Matching, Wagering = current.Wagering, Paylines = current.Paylines,
                FreeGames = current.FreeGames, SpecialPoints = current.SpecialPoints,
                Energy = new() { SymbolId = "BOLT", PointsPerVisibleSymbol = 1 },
                Math = new()
                {
                    ReelSetId = "classic-reels-v4", PaytableId = current.Math.PaytableId,
                    PaylinePayoutSteps = current.Math.PaylinePayoutSteps,
                    PayoutMultiplier = current.Math.PayoutMultiplier, Targets = current.Math.Targets
                }
            };
        }

        public ReelSetDefinition GetReelSet(string id) => new()
        {
            Id = id, SymbolSetId = GetGame(SlotSpecialRoundProfiles.ArcaneArchivesGameId).Symbols.SymbolSetId,
            Reels =
            [
                ["BOLT", "7", "POWER", "SEAL_RAND"], ["BOLT", "7", "2", "3"],
                ["4", "5", "6", "2"], ["3", "4", "5", "6"], ["2", "3", "4", "5"]
            ]
        };

        public SymbolSetDefinition? GetSymbolSet(string id) => provider.GetSymbolSet(id);
        public PaytableDefinition? GetPaytable(string id) => provider.GetPaytable(id);
    }

    private sealed class ArcaneFixtureRandom : IRandomIndexSource
    {
        private int _reelStopsRemaining = 5;
        public int Next(int maximumExclusive) => _reelStopsRemaining-- > 0 ? 0 : maximumExclusive - 1;
    }

    private sealed class SeededRandom(int seed) : IRandomIndexSource
    {
        private readonly Random _random = new(seed);
        public int Next(int maximumExclusive) => _random.Next(maximumExclusive);
    }
}
