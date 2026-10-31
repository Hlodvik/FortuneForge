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
        var settledPayout = EnergyBonus.Settle(0, result.EnergyAwarded, result.Payout).Payout;
        var catalogueGameId = FortuneForge.Server.Activity.AcceptedGameActivityReader.SlotIds[result.GameId];
        var piratesGemEvidence = result.GameId == SlotSpecialRoundProfiles.PiratesFortuneGameId
            ? PiratesGemEvidence(result) : null;
        return SinglePlayerRoundRecord.Create(catalogueGameId, result.SpinId.ToString("N"),
            "audit-test-player", 100, 100, DateTime.UtcNow, DateTime.UtcNow,
            "slotSpinResults", result.SpinId.ToString("N"),
            new { result, engineAudit = result.EngineAudit, settledPayout, piratesGemEvidence },
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
        OptionsSlotsDefinitionProvider provider, string gameId)
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
        var result = service.Spin(gameId, wager, "audit-test-player", false);
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

    private sealed class SeededRandom(int seed) : IRandomIndexSource
    {
        private readonly Random _random = new(seed);
        public int Next(int maximumExclusive) => _random.Next(maximumExclusive);
    }
}
