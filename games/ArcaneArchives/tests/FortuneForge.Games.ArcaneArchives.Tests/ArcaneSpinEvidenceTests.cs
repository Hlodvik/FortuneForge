using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.ArcaneArchives.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.ArcaneArchives.Tests;

public sealed class ArcaneSpinEvidenceTests
{
    [Fact]
    public void NativeEvidenceReplaysAndBindsThePublicResultSettlementAndWalletSummary()
    {
        var report = CreateReport();
        using var document = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = document.RootElement.GetProperty("data");
        Assert.Empty(ArcaneSpinEvidence.InspectNative(report.Round.Audit.Summary, data));

        var changed = JsonNode.Parse(data.GetRawText())!;
        changed["settledPayout"]!["totalPoints"] = changed["settledPayout"]!["totalPoints"]!.GetValue<long>() + 1;
        using var tampered = JsonDocument.Parse(changed.ToJsonString());
        Assert.Contains(ArcaneSpinEvidence.InspectNative(report.Round.Audit.Summary, tampered.RootElement),
            finding => finding.Code == "slot-settlement-mismatch");

        var participant = Assert.Single(report.Round.Audit.Summary.Participants);
        var summary = report.Round.Audit.Summary with { Participants = [participant with { PayoutCents = participant.PayoutCents + 1 }] };
        Assert.Contains(ArcaneSpinEvidence.InspectNative(summary, data), finding => finding.Code == "slot-arcane-financial-mismatch");
    }

    [Fact]
    public void NativeReplayRejectsTamperedOutcomeEvenIfItsCopiedDigestIsRecomputed()
    {
        var report = CreateReport();
        var changed = JsonNode.Parse(report.Round.Audit.PayloadJson)!["data"]!;
        var outcome = changed["nativeArcaneRound"]!["outcome"]!.Deserialize<ClassicSlotSpinOutcome>(RoundAuditCapture.Json)!;
        var forged = outcome with { Payout = outcome.Payout with { TotalPoints = outcome.Payout.TotalPoints + 1 } };
        var hash = GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(forged, RoundAuditCapture.Json));
        changed["nativeArcaneRound"]!["outcome"] = JsonSerializer.SerializeToNode(forged, RoundAuditCapture.Json);
        changed["nativeArcaneRound"]!["outcomeSha256"] = hash;
        changed["engineOutcomeSha256"] = hash;
        using var tampered = JsonDocument.Parse(changed.ToJsonString());
        Assert.Contains(ArcaneSpinEvidence.InspectNative(report.Round.Audit.Summary, tampered.RootElement),
            finding => finding.Code == "slot-arcane-native-proof-mismatch");
    }

    [Fact]
    public void RetiredHostReceiptIsUnsupportedInsteadOfPassingThroughGenericMath()
    {
        var report = CreateReport();
        var old = report.Round.Audit.Summary with { AuditorVersion = "slot-outcome-auditor-v1" };
        using var document = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var finding = Assert.Single(ArcaneSpinEvidence.InspectNative(old, document.RootElement.GetProperty("data")));
        Assert.Equal("slot-owned-evidence-unsupported", finding.Code);
        Assert.Equal("warning", finding.Severity);
    }

    private static ArcaneSpinReporting CreateReport()
    {
        const long wager = 12;
        const string account = "isolated-native-audit";
        var started = DateTime.UtcNow;
        var before = new ArcaneFeatureAdmission(ArcaneTome.Empty, ArcanePotions.Empty, true);
        var output = new ArcaneArchivesEngine(new FixtureRandom()).SpinTome(wager, account, 0, 0, false, false);
        var hash = GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(output, RoundAuditCapture.Json));
        var round = new ArcanePreparedRound(Guid.NewGuid(), account, new("native-audit-operation-0001", wager, false),
            before, output, hash, checked((long)(wager * output.PointValueInCents)),
            checked((long)(output.Payout.TotalPoints * output.PointValueInCents)), started);
        var features = new ArcaneFeatureSettlement(ArcaneTome.CompleteSpin(before.Tome!, output.Reels, false),
            ArcanePotions.CompleteSpin(before.Potions!, ArcaneTome.OrderedIngredientUnits(before.Tome!, output.Reels, false),
                false, wager, allowRareRecipes: ArcaneArchivesEngine.SupportsRarePotions(output.Audit.EngineVersion)));
        return ArcaneSpinReport.Capture(round, features, 100m, "native-wallet-receipt", DateTime.UtcNow);
    }

    private sealed class FixtureRandom : IGameRandomSource
    {
        private readonly Random random = new(426);
        public int Next(int exclusiveUpperBound) => random.Next(exclusiveUpperBound);
    }
}
