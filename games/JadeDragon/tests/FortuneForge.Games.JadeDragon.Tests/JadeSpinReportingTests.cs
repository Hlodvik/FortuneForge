using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.JadeDragon.Server;
using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.JadeDragon.Tests;

public sealed class JadeSpinReportingTests
{
    private const string Account = "jade-spins-proof-test";

    [Fact]
    public void CascadeReportChargesQuarterRandPointsAndReportsPayoutInWholeCents()
    {
        var request = new JadeRoundRequest("jade-test-cascade-proof", 20, false);
        var before = new JadeRoundBefore(0, 0, null, 13, 25, 3,
            new Dictionary<string, long>(), new Dictionary<string, long>());
        var round = JadeRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null,
            new ScriptedDraws([0,0,96,96,0,82,96,35,0,82,50,66,50,82,21,35,50,66,35,21,21,50,66,35]));
        var result = Settle(round);
        var publicSpin = JadeSpinResponse.From(result);
        var report = JadeSpinReport.Capture(round, result, result.Wallet.SettledAtUtc.UtcDateTime);
        Assert.Equal(500, publicSpin.ChargedWagerCents);
        Assert.Equal(25, publicSpin.PointValueInCents);
        Assert.Equal(1, publicSpin.PayoutPointValueInCents);
        Assert.Equal(47, publicSpin.Payout.TotalPoints);
        Assert.Empty(publicSpin.Payout.Paylines);
        Assert.Empty(publicSpin.ReelStops);
        Assert.Equal(10, publicSpin.FreeSpinsRemaining);
        Assert.Equal(25, publicSpin.EnergyBalance);
        Assert.Equal(13, publicSpin.SpecialPointsBalance);
        Assert.Equal(95.47m, publicSpin.SlotsCreditsBalance);
        Assert.Equal(.47, report.Result["wonSlotsCredits"]);
        Assert.Equal("empty-slots-game", report.Round.Audit.Summary.GameId);
        Assert.Equal(round.OutcomeSha256, report.Result["engineOutcomeSha256"]);
        Assert.True(report.Result.ContainsKey("jadeDragonCascadeSettlementJson"));

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Equal(JadeDragonCascadeRules.Version, data.GetProperty("admittedMathModelId").GetString());
        Assert.Equal(round.Outcome.Cascade!.DrawIndexes, data.GetProperty("engineAudit")
            .GetProperty("jadeDragonCascadeDraws").Deserialize<int[]>());
        var differentPayout = round.SettledPayout with { TotalPoints = round.SettledPayout.TotalPoints + 1 };
        Assert.Throws<InvalidOperationException>(() => JadeSpinEvidence.Validate(round with { SettledPayout = differentPayout }));
    }

    [Fact]
    public void LegacyCarriesKeepExpandedPaylinesAndReplayTheSeparateAuditAfterSerialization()
    {
        var request = new JadeRoundRequest("jade-test-legacy-proof", 500, true);
        var before = new JadeRoundBefore(0, 2,
            new(2, 500, JadeDragonCollections.CollectionFeatureMode, JadeDragonEngine.Version), 13, 25, 3,
            new Dictionary<string, long>(), new Dictionary<string, long>());
        var round = JadeRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null, new SeededDraws());
        var restored = JsonSerializer.Deserialize<JadePreparedRound>(
            JsonSerializer.Serialize(round, RoundAuditCapture.Json), RoundAuditCapture.Json)!;
        Assert.Null(restored.Outcome.Legacy!.EngineAudit);
        Assert.NotNull(restored.Outcome.LegacyAudit);
        Assert.NotEmpty(restored.Outcome.LegacyDrawIndexes!);
        JadeRoundPolicy.Validate(restored);

        var result = Settle(restored);
        var reply = JadeSpinResponse.From(result);
        var report = JadeSpinReport.Capture(restored, result, result.Wallet.SettledAtUtc.UtcDateTime);
        Assert.Equal(500, reply.WagerPoints);
        Assert.Equal(0, reply.ChargedWagerCents);
        Assert.Null(reply.PayoutPointValueInCents);
        Assert.Null(reply.JadeDragonCascade);
        Assert.Equal(5, reply.ReelStops.Count);
        Assert.All(reply.Reels, reel => Assert.Equal(6, reel.Count));
        Assert.Equal(0, reply.EnergyBalance);
        Assert.Equal((double)(reply.Payout.TotalPoints * 25m / 100m), report.Result["wonSlotsCredits"]);
        Assert.True(report.Result.ContainsKey("slotSettlementJson"));
        Assert.False(report.Result.ContainsKey("payoutPointValueInCents"));
        var participant = Assert.Single(report.Round.Audit.Summary.Participants);
        Assert.Equal(0, participant.WagerCents);
        Assert.Equal(reply.Payout.TotalPoints * 25, participant.PayoutCents);

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(JadeSpinEvidence.InspectNativeLegacy(report.Round.Audit.Summary, data));
        var audit = data.GetProperty("engineAudit");
        Assert.Equal(JadeDragonEngine.Version, audit.GetProperty("engineVersion").GetString());
        Assert.Equal(25, audit.GetProperty("energyBalanceBefore").GetInt64());
        Assert.False(audit.GetProperty("isDerivedFreeSpin").GetBoolean());
        Assert.Equal(restored.Outcome.LegacyDrawIndexes, audit.GetProperty("jadeDragonLegacyDraws").GetBytesFromBase64());
        var altered = restored.Outcome.LegacyDrawIndexes!.ToArray();
        altered[0] = byte.MaxValue;
        Assert.Throws<InvalidOperationException>(() => JadeSpinEvidence.Validate(restored with
        { Outcome = restored.Outcome with { LegacyDrawIndexes = altered } }));
        var archived = JsonNode.Parse(data.GetRawText())!.AsObject();
        archived["engineAudit"]!["jadeDragonLegacyDraws"] = Convert.ToBase64String(altered);
        Assert.Equal("slot-jade-legacy-native-proof-mismatch", Assert.Single(JadeSpinEvidence.InspectNativeLegacy(
            report.Round.Audit.Summary, JsonSerializer.SerializeToElement(archived))).Code);
        archived["engineAudit"]!.AsObject().Remove("jadeDragonLegacyDraws");
        Assert.Empty(JadeSpinEvidence.InspectNativeLegacy(report.Round.Audit.Summary, JsonSerializer.SerializeToElement(archived)));
    }

    [Fact]
    public void LegacyComputeWorkerWireKeepsTheAuditSeparateFromItsPublicResult()
    {
        var request = new PaidSlotOutcomeRequest(Guid.NewGuid(), JadeDragonEngine.GameId, Account,
            500, true, false, 25, JadeDragonCollections.CollectionFeatureMode, 3, null);
        var wire = new JadePaidSlotOutcomes().Resolve(request);
        Assert.Equal(request.AdmissionId, wire.AdmissionId);
        Assert.Equal(JadeDragonEngine.Version, wire.OutcomeKind);
        var outcome = Assert.IsType<JadeLegacyOutcome>(wire.Outcome.Deserialize<JadeLegacyOutcome>(RoundAuditCapture.Json));
        Assert.Null(outcome.Result.EngineAudit);
        Assert.True(outcome.Result.IsFreeSpin);
        Assert.Equal(500, outcome.Result.WagerPoints);
        Assert.All(outcome.Result.Reels, reel => Assert.Equal(6, reel.Count));
        Assert.Equal(JadeDragonEngine.Version, outcome.Audit.EngineVersion);
        Assert.Equal(JadeDragonCollections.CollectionFeatureMode, outcome.Audit.FreeSpinFeatureMode);
        Assert.Equal(25, outcome.Audit.EnergyBalanceBefore);
        Assert.Equal(3, outcome.Audit.PityMissesBefore);
    }

    private static JadeRoundResult Settle(JadePreparedRound round)
    {
        const long balanceBefore = 10_000;
        var spin = round.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(JadeDragonEngine.GameId, Account, spin),
            round.WagerCents > 0 ? $"{spin}-wager" : null, round.PayoutCents > 0 ? $"{spin}-payout" : null,
            balanceBefore, balanceBefore - round.WagerCents, balanceBefore - round.WagerCents + round.PayoutCents,
            DateTimeOffset.UtcNow, false, false, LegacyFreeGameTransactionId: round.SpendsLegacy ? $"{spin}-free-game-use" : null,
            LegacyFreeGamesRemaining: round.Progress.After.LegacyRemaining);
        return new(JadeRoundStateReply.From(round.Progress.After), receipt) { Round = round, ChargedWagerCents = round.WagerCents };
    }

    private sealed class ScriptedDraws(int[] indexes) : IGameRandomSource
    {
        private int position;
        public int Next(int maximumExclusive) => indexes[position++];
    }

    private sealed class SeededDraws : IGameRandomSource
    {
        private readonly Random random = new(73);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
