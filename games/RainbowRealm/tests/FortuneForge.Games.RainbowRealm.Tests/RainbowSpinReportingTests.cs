using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.RainbowRealm.Server;
using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.RainbowRealm.Tests;

public sealed class RainbowSpinReportingTests
{
    private const string Account = "rainbow-report-compatibility";

    [Fact]
    public void CharmHarvestAloneIsAWinWithNoPaylineAndSettlesExactCents()
    {
        var before = new RainbowRoundBefore(0, 0, null, 0, 0, Empty(), Empty());
        var request = new RainbowRoundRequest("rainbow-harvest-proof", 100, false);
        var random = new SeededDraws();
        var round = Enumerable.Range(0, 512)
            .Select(_ => RainbowRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null, random))
            .First(sample => sample.Outcome.CollectorPoints > 0 && sample.Outcome.Payout.Paylines.Count == 0);
        var settled = Settle(round);
        var response = RainbowSpinResponse.From(settled);
        var report = RainbowSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.Empty(response.Payout.Paylines);
        Assert.Equal(response.MoneyGrabPoints, response.Payout.TotalPoints);
        var expectedHarvest = RainbowRealmCharmHarvest.Calculate(response.Reels, response.WagerPoints, 1m);
        Assert.Equal(expectedHarvest.PawCount, response.MonkeyPawCount);
        Assert.Equal(expectedHarvest.CollectorPoints, response.MoneyGrabPoints);
        Assert.Equal(response.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.Equal(0, response.EnergyBalance);
        Assert.False(response.EnergyMultiplierApplied);
        Assert.Equal(1m, response.PayoutMultiplier);
        Assert.Equal("win", report.Result["result"]);
        Assert.Equal("rainbow-realm-fruits-v1", report.Result["gameId"]);
        Assert.Equal((double)(round.PayoutCents / 100m), report.Result["wonSlotsCredits"]);
        var payoutBreakdown = Assert.IsType<Dictionary<string, object>>(report.Result["payoutBreakdown"]);
        Assert.Equal(response.MoneyGrabPoints, payoutBreakdown["totalPoints"]);
        Assert.Empty(Assert.IsType<object[]>(payoutBreakdown["paylines"]));
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, id => id.Contains("energy-", StringComparison.Ordinal));
        Assert.False(JsonSerializer.SerializeToElement(response, RoundAuditCapture.Json).TryGetProperty("chargedWagerCents", out _));

        var summary = report.Round.Audit.Summary;
        Assert.Equal("rainbow-realm", summary.GameId);
        var participant = Assert.Single(summary.Participants);
        Assert.Equal(2_500, participant.WagerCents);
        Assert.Equal(round.PayoutCents, participant.PayoutCents);
        using var frozen = JsonDocument.Parse((string)report.Result["slotSettlementJson"]);
        Assert.Equal(response.Payout.TotalPoints, frozen.RootElement.GetProperty("Payout").GetProperty("TotalPoints").GetInt64());
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Equal(2_500, data.GetProperty("result").GetProperty("chargedWagerCents").GetInt64());
        Assert.Empty(RainbowSpinEvidence.InspectNative(summary, data));

        var altered = JsonNode.Parse(data.GetRawText())!.AsObject();
        altered["result"]!["moneyGrabPoints"] = response.MoneyGrabPoints - 1;
        AssertMismatch(summary, altered);
        altered["result"]!["moneyGrabPoints"] = response.MoneyGrabPoints;
        altered["settledPayout"]!["totalPoints"] = 0;
        AssertMismatch(summary, altered);
        altered["settledPayout"]!["totalPoints"] = response.Payout.TotalPoints;
        altered["rainbowDraws"] = Convert.ToBase64String(round.Engine.Draws[..^1]);
        AssertMismatch(summary, altered);
    }

    [Fact]
    public void OddLegacyWagerCanReearnScatterAndCollectionSpinsWithOneLegacyDebit()
    {
        var before = new RainbowRoundBefore(0, 2, new(2, 101, null), 0, 0,
            new Dictionary<string, long> { ["sync"] = 59 }, new Dictionary<string, long> { ["sync"] = 59 * 101L });
        var request = new RainbowRoundRequest("rainbow-legacy-retrigger-proof", 101, true);
        var strips = RainbowRealmEngine.CreateReelSet().Reels;
        var stops = strips.Select((strip, reel) => Enumerable.Range(256, strip.Count - 256)
            .First(stop => strip[stop] == (reel == 0 ? "SEAL_SYNC" : "FREE"))).ToArray();
        var round = RainbowRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null, new ScriptedDraws(stops));
        var settled = Settle(round);
        var response = RainbowSpinResponse.From(settled);
        var report = RainbowSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.True(response.IsFreeSpin);
        Assert.Equal(101, response.WagerPoints);
        Assert.Equal(101, response.FreeSpinWagerPoints);
        Assert.Equal(21, response.FreeSpinsRemaining);
        Assert.Equal("sync", response.FreeSpinFeatureMode);
        Assert.Equal(10, response.FreeSpinsAwarded);
        Assert.Equal(10, report.Result["sealFreeSpinsAwarded"]);
        Assert.Equal(0, round.WagerCents);
        Assert.Equal(1, settled.Wallet.LegacyFreeGamesRemaining);
        Assert.Equal($"{round.Outcome.SpinId:N}-free-game-use", settled.Wallet.LegacyFreeGameTransactionId);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, id => id.EndsWith("-free-game-use", StringComparison.Ordinal));
        var awardId = $"{round.Outcome.SpinId:N}-free-games-award";
        var witness = report.GameplayWitnesses[awardId];
        Assert.Equal(awardId, witness["transactionId"]);
        Assert.Equal("freeGames:rainbow-realm-fruits-v1", witness["currencyId"]);
        Assert.Equal(20L, witness["amount"]);
        Assert.Equal(21L, witness["balanceAfter"]);
        Assert.Equal(stops[0], BinaryPrimitives.ReadUInt16LittleEndian(round.Engine.Draws));

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        var summary = report.Round.Audit.Summary;
        Assert.Empty(RainbowSpinEvidence.InspectNative(summary, data));
        var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        changed["rainbowProgress"]!["walletLegacyRemaining"] = 2;
        AssertMismatch(summary, changed);
        changed.Remove("rainbowDraws");
        changed.Remove("rainbowProgress");
        changed.Remove("engineOutcomeSha256");
        changed.Remove("walletSettlementId");
        var historical = JsonSerializer.SerializeToElement(changed);
        Assert.False(RainbowSpinEvidence.IsNative(historical));
        Assert.Empty(RainbowSpinEvidence.InspectNative(summary, historical));
    }

    private static RainbowRoundResult Settle(RainbowPreparedRound round)
    {
        const long before = 50_000;
        var spend = before - round.WagerCents;
        var spin = round.Outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(RainbowRealmEngine.GameId, Account, spin),
            round.WagerCents > 0 ? $"{spin}-wager" : null, round.PayoutCents > 0 ? $"{spin}-payout" : null,
            before, spend, spend + round.PayoutCents, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGameTransactionId: round.SpendsLegacy ? $"{spin}-free-game-use" : null,
            LegacyFreeGamesRemaining: round.Progress.After.LegacyRemaining);
        return new(RainbowRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        {
            Round = round,
            ChargedWagerCents = round.WagerCents
        };
    }

    private static void AssertMismatch(RoundAuditSummary summary, JsonObject data) =>
        Assert.Equal("slot-rainbow-native-proof-mismatch", Assert.Single(RainbowSpinEvidence.InspectNative(
            summary, JsonSerializer.SerializeToElement(data))).Code);

    private static IReadOnlyDictionary<string, long> Empty() => new Dictionary<string, long>();

    private sealed class ScriptedDraws(int[] stops) : IGameRandomSource
    {
        private int position;
        public int Next(int maximumExclusive) => position < stops.Length ? stops[position++] : 0;
    }

    private sealed class SeededDraws : IGameRandomSource
    {
        private readonly Random random = new(12409);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }
}
