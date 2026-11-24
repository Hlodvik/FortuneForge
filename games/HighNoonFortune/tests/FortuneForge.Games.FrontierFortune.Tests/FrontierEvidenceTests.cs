using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.FrontierFortune.Server;
using Xunit;

namespace FortuneForge.Games.FrontierFortune.Tests;

public sealed class FrontierEvidenceTests
{
    private static (RoundAuditSummary Summary, JsonElement Data) Evidence()
    {
        var round = FrontierRoundPolicy.Prepare("evidence-player", new("evidence-operation-0001", "spin", 4),
            FrontierAccountState.Empty(), new FrontierEngineTests.WantedRandom(), DateTime.UtcNow);
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(FrontierFortuneEngine.GameId,
            round.AccountId, round.AdmissionId), "wager-proof", null, 100_000, 99_900, 99_900, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGamesRemaining: 0);
        var capture = RoundAuditCapture.Create("high-noon-fortune", round.AdmissionId, round.AdmissionId, 1,
            round.StartedAtUtc, DateTime.UtcNow, new { contractVersion = 1, kind = "frontier-fortune", round, wallet = receipt },
            "frontier-fortune-v1", "frontier-replay-v1",
            [new(round.AccountId, "Player", true, round.WagerCents, round.PayoutCents, round.PayoutCents - round.WagerCents)], []);
        return (capture.Summary, JsonDocument.Parse(capture.PayloadJson).RootElement.Clone());
    }
    [Fact]
    public void NativeEvidenceReplaysAndVerifiesItsExactWalletAndSummary()
    {
        var evidence = Evidence();
        Assert.True(FrontierEvidence.IsNative(evidence.Summary));
        Assert.Empty(FrontierEvidence.Inspect(evidence.Summary, evidence.Data));
    }
    [Fact]
    public void NativeEvidenceRejectsAlteredMoneyParticipantIdentityAndDraws()
    {
        var (summary, data) = Evidence();
        Assert.NotEmpty(FrontierEvidence.Inspect(summary with { RoundId = "other" }, data));
        Assert.NotEmpty(FrontierEvidence.Inspect(summary with { Participants = [summary.Participants[0] with { PayoutCents = 100 }] }, data));
        var payload = System.Text.Json.Nodes.JsonNode.Parse(data.GetRawText())!;
        payload["round"]!["spin"]!["draws"]![0]!["value"] = 0;
        Assert.NotEmpty(FrontierEvidence.Inspect(summary, JsonSerializer.SerializeToElement(payload)));
        payload = System.Text.Json.Nodes.JsonNode.Parse(data.GetRawText())!;
        payload["wallet"]!["balanceAfterCents"] = 1_000_000;
        Assert.NotEmpty(FrontierEvidence.Inspect(summary, JsonSerializer.SerializeToElement(payload)));
    }
}
