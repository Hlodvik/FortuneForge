using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.GodsOfOlympus.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Xunit;

namespace FortuneForge.Games.GodsOfOlympus.Tests;

public sealed class OlympusSpinReportingTests
{
    private const string Account = "olympus-report-compatibility";

    [Fact]
    public void CurrentTumbleKeepsBothBoardsAndMultiplierWithExactIntegerDrawsAndCents()
    {
        var before = new ClassicSlotRoundState(0, 0, null, 11, 55, 7,
            new Dictionary<string, long> { ["sync"] = 7 }, new Dictionary<string, long> { ["sync"] = 14 });
        var indexes = DrawsFor(OlympusTumbleRules.EngineVersion);
        var round = OlympusRoundPolicy.Prepare(Account, new("olympus-tumble-native-test", 6, false), before,
            DateTime.UtcNow, null, new ScriptedDraws(indexes));
        var result = Settle(round);
        var response = OlympusSpinResponse.From(result);
        var report = OlympusSpinReport.Capture(round, result, round.Engine.GeneratedAtUtc);

        Assert.Equal(OlympusTumbleRules.EngineVersion, response.ReelSetId);
        Assert.Equal(response.ReelSetId, response.PaytableId);
        Assert.Equal(6, response.Reels.Count);
        Assert.All(response.Reels, reel => Assert.Equal(5, reel.Count));
        Assert.Equal(new int[6], response.ReelStops);
        var tumble = Assert.IsType<OlympusTumbleOutcome>(response.OlympusTumble);
        Assert.Equal(2, tumble.Steps.Count);
        Assert.Equal(15, Assert.Single(tumble.Steps[0].Awards).AmountPoints);
        Assert.Equal(3, Assert.Single(tumble.Steps[1].Awards).AmountPoints);
        Assert.Equal(18, tumble.BaseTotalPoints);
        Assert.Equal(2, tumble.Multiplier);
        Assert.Equal(36, tumble.TotalPoints);
        Assert.Equal(36, response.Payout.TotalPoints);
        Assert.Empty(response.Payout.Paylines);
        Assert.Equal(107.50m, response.SlotsCreditsBalance);
        Assert.Equal(11, response.SpecialPointsBalance);
        Assert.Equal(55, response.EnergyBalance);
        Assert.False(response.EnergyMultiplierApplied);
        Assert.Equal(1m, response.PayoutMultiplier);
        Assert.Equal(7, response.ConsecutiveFiveMisses);
        Assert.Equal(7, Assert.Single(response.SealCollections, value => value.SealId == "sync").Count);
        Assert.Empty(report.GameplayWitnesses);
        Assert.True(report.Result.ContainsKey("olympusTumbleSettlementJson"));
        Assert.Equal(OlympusTumbleRules.EngineVersion, report.Result["mathModelId"]);
        Assert.Equal("gods-of-olympus-v1", report.Result["gameId"]);
        Assert.Equal(1.5d, report.Result["wageredSlotsCredits"]);
        Assert.Equal(9d, report.Result["wonSlotsCredits"]);
        Assert.Equal(7.5d, report.Result["netSlotsCredits"]);

        var summary = report.Round.Audit.Summary;
        Assert.Equal("gods-of-olympus", summary.GameId);
        var participant = Assert.Single(summary.Participants);
        Assert.Equal(150, participant.WagerCents);
        Assert.Equal(900, participant.PayoutCents);
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        var audit = data.GetProperty("engineAudit");
        Assert.Equal(55, audit.GetProperty("energyBalanceBefore").GetInt64());
        Assert.Equal(0, data.GetProperty("result").GetProperty("energyBalance").GetInt64());
        Assert.Equal(150, data.GetProperty("result").GetProperty("chargedWagerCents").GetInt64());
        Assert.False(JsonSerializer.SerializeToElement(response, RoundAuditCapture.Json).TryGetProperty("chargedWagerCents", out _));
        Assert.Equal(indexes, audit.GetProperty("olympusTumble").GetProperty("drawIndexes").Deserialize<int[]>());
        Assert.True(indexes[15] > ushort.MaxValue);
        Assert.Empty(OlympusSpinEvidence.InspectNative(summary, proof.RootElement, data));

        var changed = JsonNode.Parse(proof.RootElement.GetRawText())!.AsObject();
        var changedAudit = changed["data"]!["engineAudit"]!["olympusTumble"]!;
        changedAudit["drawIndexes"]![15] = indexes[15] + 1;
        AssertMismatch(summary, changed);
        changedAudit["drawIndexes"]![15] = indexes[15];
        changed["data"]!["result"]!["olympusTumble"]!["steps"]![0]!["dropRows"]![0]![0] = 4;
        AssertMismatch(summary, changed);
        changed = JsonNode.Parse(proof.RootElement.GetRawText())!.AsObject();
        changed["payoutCents"] = 899;
        AssertMismatch(summary, changed);

        var old = JsonNode.Parse(data.GetRawText())!.AsObject();
        old.Remove("olympusProgress");
        old.Remove("olympusClassicDraws");
        old.Remove("engineOutcomeSha256");
        old.Remove("walletSettlementId");
        Assert.False(OlympusSpinEvidence.IsNative(JsonSerializer.SerializeToElement(old)));
    }

    [Theory]
    [InlineData(OlympusTumbleRules.EngineVersionV2, 21)]
    [InlineData(OlympusTumbleRules.EngineVersionV3, 18)]
    public void HistoricalPaidVersionsReplayOriginalDrawsWithoutV4MultiplierFields(string version, long payoutPoints)
    {
        var frozen = new GodsOfOlympusEngine(new ScriptedDraws(DrawsFor(version))).SpinTumble(6, engineVersion: version);
        var engine = new OlympusRoundEngineResult(frozen, null, null, DateTime.UtcNow);
        Assert.Equal(payoutPoints, frozen.Outcome.TotalPoints);
        Assert.Null(frozen.Outcome.BaseTotalPoints);
        Assert.Null(frozen.Outcome.Multiplier);
        OlympusSpinEvidence.Validate(engine, Account);
        Assert.Throws<InvalidOperationException>(() => OlympusSpinEvidence.Validate(engine with
        {
            Tumble = frozen with { EngineVersion = OlympusTumbleRules.LegacyEngineVersion }
        }, Account));
    }

    [Fact]
    public void ClassicCarryKeepsTheOddWagerAndPublishesNoTumbleOrExtraLegacyDebit()
    {
        var before = new ClassicSlotRoundState(0, 2, new(2, 3, "paw-rand"), 10, 34, 2,
            new Dictionary<string, long>(), new Dictionary<string, long>());
        var request = new OlympusRoundRequest("olympus-classic-legacy-report", 3, true, UseSpecialBoost: true);
        var random = new SeededDraws();
        var round = Enumerable.Range(0, 128)
            .Select(_ => OlympusRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null, random))
            .First(sample => sample.Progress.After.NativeRemaining == 0);
        var result = Settle(round);
        var response = OlympusSpinResponse.From(result);
        var report = OlympusSpinReport.Capture(round, result, round.Engine.GeneratedAtUtc);

        Assert.True(response.IsFreeSpin);
        Assert.True(response.SpecialBoostApplied);
        Assert.Equal(3, response.WagerPoints);
        Assert.Equal(3, response.FreeSpinWagerPoints);
        Assert.Equal(1, response.FreeSpinsRemaining);
        Assert.Equal("paw-rand", response.FreeSpinFeatureMode);
        Assert.Equal(5, response.Reels.Count);
        Assert.All(response.Reels, reel => Assert.Equal(4, reel.Count));
        Assert.Null(response.OlympusTumble);
        Assert.False(JsonSerializer.SerializeToElement(response, RoundAuditCapture.Json).TryGetProperty("olympusTumble", out _));
        Assert.Equal(0, round.WagerCents);
        Assert.Equal(1, result.Wallet.LegacyFreeGamesRemaining);
        Assert.Equal($"{round.Engine.SpinId:N}-free-game-use", result.Wallet.LegacyFreeGameTransactionId);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, key => key.EndsWith("-free-game-use", StringComparison.Ordinal));
        var boost = report.GameplayWitnesses[$"{round.AdmissionId:N}-special-boost-use"];
        Assert.Equal("specialPoints:gods-of-olympus-v1", boost["currencyId"]);
        Assert.Equal(-8L, boost["amount"]);
        Assert.Equal(2L, boost["balanceAfter"]);
        Assert.Equal(GodsOfOlympusEngine.ClassicEngineVersion, report.Result["engineVersion"]);
        Assert.True(report.Result.ContainsKey("slotSettlementJson"));

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var summary = report.Round.Audit.Summary;
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(OlympusSpinEvidence.InspectNative(summary, proof.RootElement, data));
        Assert.Equal(round.Engine.ClassicDraws, data.GetProperty("olympusClassicDraws").GetBytesFromBase64());
        Assert.Equal("paw-rand", data.GetProperty("activeFreeSpinFeatureMode").GetString());
        var altered = JsonNode.Parse(proof.RootElement.GetRawText())!.AsObject();
        altered["data"]!["olympusProgress"]!["walletLegacyRemaining"] = 2;
        AssertMismatch(summary, altered);
    }

    private static int[] DrawsFor(string version)
    {
        var others = new[] { 0, 34, 51, 77, 85, 91, 96 };
        var indexes = Enumerable.Repeat(66, 8).Concat(Enumerable.Repeat(17, 7))
            .Concat(Enumerable.Range(0, 15).Select(index => others[index % others.Length]))
            .Append(17).Concat(Enumerable.Range(0, 15).Select(index => others[index % others.Length])).ToArray();
        if (version != OlympusTumbleRules.EngineVersion) return indexes;
        var weighted = indexes.Select(index => index * 2000).ToArray();
        weighted[15] = 200_000;
        return weighted;
    }

    private static OlympusRoundResult Settle(OlympusPreparedRound round)
    {
        const long balanceBefore = 10_000;
        var spend = balanceBefore - round.WagerCents;
        var spin = round.Engine.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(GodsOfOlympusEngine.GameId, Account, spin),
            round.WagerCents > 0 ? $"{spin}-wager" : null, round.PayoutCents > 0 ? $"{spin}-payout" : null,
            balanceBefore, spend, spend + round.PayoutCents, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGameTransactionId: round.SpendsLegacy ? $"{spin}-free-game-use" : null,
            LegacyFreeGamesRemaining: round.Progress.After.LegacyRemaining);
        return new(OlympusRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        {
            Round = round,
            ChargedWagerCents = round.WagerCents
        };
    }

    private static void AssertMismatch(RoundAuditSummary summary, JsonObject original)
    {
        var root = JsonSerializer.SerializeToElement(original);
        Assert.Equal("slot-olympus-native-proof-mismatch", Assert.Single(OlympusSpinEvidence.InspectNative(
            summary, root, root.GetProperty("data"))).Code);
    }

    private sealed class ScriptedDraws(int[] indexes) : IGameRandomSource
    {
        private int cursor;
        public int Next(int maximumExclusive) => indexes[cursor++];
    }

    private sealed class SeededDraws : IGameRandomSource
    {
        private readonly Random random = new(3697);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
