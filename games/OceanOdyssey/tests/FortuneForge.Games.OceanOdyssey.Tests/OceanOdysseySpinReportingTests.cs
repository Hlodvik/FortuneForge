using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.OceanOdyssey.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using FortuneForge.Games.Slots.Runtime;
using Xunit;

namespace FortuneForge.Games.OceanOdyssey.Tests;

public sealed class OceanOdysseySpinReportingTests
{
    private const string Account = "ocean-odyssey-report-compatibility";

    [Fact]
    public void ThreeScattersFollowThePublishedThreshold()
    {
        var game = OceanOdysseyEngine.CreateGame();
        Assert.Equal(23, game.Layout.PaylineCount);
        Assert.Equal(4, game.FreeGames!.RequiredSymbols);
        Assert.Equal(6, game.FreeGames.AwardedSpins);
        Assert.Equal(28, OceanOdysseyEngine.Profile.CollectionTarget);
        var stops = OceanOdysseyEngine.CreateReelSet().Reels.Select((reel, index) => index < 3 ? reel.IndexOf("FREE") : 0).ToArray();
        var outcome = new OceanOdysseyEngine(new ScriptedDraws(stops)).Spin(2, Account, persistedPityMisses: 0);
        Assert.Equal(3, outcome.Reels.SelectMany(reel => reel).Count(symbol => symbol == "FREE"));
        Assert.Equal(0, outcome.FreeSpinsAwarded);
    }

    [Fact]
    public void EnergyMultipliesThePatternWinAndAwardsTheOriginalCollectionMode()
    {
        var before = new ClassicSlotRoundState(0, 0, null, 0, 99, 0,
            new Dictionary<string, long> { ["sync"] = 7 }, new Dictionary<string, long> { ["sync"] = 28 });
        var request = new OceanOdysseyRoundRequest("ocean-odyssey-energy-receipt", 4, false);
        var stops = new int[5];
        stops[4] = OceanOdysseyEngine.CreateReelSet().Reels[4].IndexOf("BOLT");
        var round = OceanOdysseyRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null, new ScriptedDraws(stops));
        var settled = Settle(round);
        var response = OceanOdysseySpinResponse.From(settled);
        var report = OceanOdysseySpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.True(response.EnergyMultiplierApplied);
        Assert.Equal(1.5m, response.PayoutMultiplier);
        Assert.Equal(0, response.EnergyBalance);
        Assert.Equal(6, response.FreeSpinsRemaining);
        Assert.Equal("sync-rand", response.FreeSpinFeatureMode);
        Assert.Equal(4, response.FreeSpinWagerPoints);
        Assert.Equal(0, response.FreeSpinsAwarded);
        Assert.Equal(6, report.Result["sealFreeSpinsAwarded"]);
        Assert.Equal("sync-rand", report.Result["sealFreeSpinFeatureMode"]);
        Assert.Equal(0, response.MonkeyPawCount);
        Assert.Equal(0, response.MoneyGrabPoints);
        Assert.DoesNotContain(response.Payout.Paylines, line => line.PaylineId == 901);
        Assert.Equal(13, response.BananaBonusPoints);
        Assert.Equal(20, Assert.Single(response.Payout.Paylines, line => line.PaylineId == 801).AmountPoints);
        Assert.Equal(round.Outcome.Payout.Paylines.Sum(line => line.Matches.Sum(match => Multiply(match.AmountPoints))),
            response.Payout.TotalPoints);
        Assert.Equal(response.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.Equal((double)(round.PayoutCents / 100m), report.Result["wonSlotsCredits"]);
        Assert.Equal("ocean-odyssey-v1", report.Result["gameId"]);
        Assert.False(JsonSerializer.SerializeToElement(response, RoundAuditCapture.Json).TryGetProperty("chargedWagerCents", out _));

        var spin = round.Outcome.SpinId.ToString("N");
        AssertWitness(report, $"{spin}-energy-award", "energy:ocean-odyssey-v1", 1, 100);
        AssertWitness(report, $"{spin}-energy-multiplier-use", "energy:ocean-odyssey-v1", -100, 0);
        AssertWitness(report, $"{spin}-free-games-award", "freeGames:ocean-odyssey-v1", 6, 6);
        var summary = report.Round.Audit.Summary;
        Assert.Equal("ocean-odyssey", summary.GameId);
        var participant = Assert.Single(summary.Participants);
        Assert.Equal(100, participant.WagerCents);
        Assert.Equal(round.PayoutCents, participant.PayoutCents);
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        var audit = data.GetProperty("engineAudit");
        Assert.Equal(23, audit.GetProperty("baseGame").GetProperty("layout").GetProperty("paylineCount").GetInt32());
        Assert.Equal(1.071m, audit.GetProperty("featureProfile").GetProperty("payoutMultiplier").GetDecimal());
        Assert.False(audit.GetProperty("featureProfile").GetProperty("usesDirectValueTokens").GetBoolean());
        Assert.Empty(OceanOdysseySpinEvidence.InspectNative(summary, data));

        var changed = JsonNode.Parse(data.GetRawText())!.AsObject();
        changed["oceanOdysseyProgress"]!["collectionFreeSpinWagerPoints"] = 6;
        AssertMismatch(summary, changed);
        changed["oceanOdysseyProgress"]!["collectionFreeSpinWagerPoints"] = 4;
        changed["oceanOdysseyDraws"] = Convert.ToBase64String(round.Engine.Draws.Append((byte)0).ToArray());
        AssertMismatch(summary, changed);
    }

    [Fact]
    public void OddImportedWagerRetriggersSixScatterSpinsAndUsesOnlyOneLegacyDebit()
    {
        var before = new ClassicSlotRoundState(0, 2, new(2, 3, "sync-rand"), 0, 0, 0,
            new Dictionary<string, long>(), new Dictionary<string, long>());
        var stops = OceanOdysseyEngine.CreateReelSet().Reels.Select(reel => reel.IndexOf("FREE")).ToArray();
        var round = OceanOdysseyRoundPolicy.Prepare(Account, new("ocean-odyssey-imported-odd-wager", 3, true), before,
            DateTime.UtcNow, null, new ScriptedDraws(stops));
        var settled = Settle(round);
        var response = OceanOdysseySpinResponse.From(settled);
        var report = OceanOdysseySpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.True(response.IsFreeSpin);
        Assert.Equal(3, response.WagerPoints);
        Assert.Equal(3, response.FreeSpinWagerPoints);
        Assert.All(response.Reels, reel => Assert.Equal(4, reel.Count));
        Assert.Equal(6, response.FreeSpinsAwarded);
        Assert.Equal(7, response.FreeSpinsRemaining);
        Assert.Equal("sync-rand", response.FreeSpinFeatureMode);
        Assert.Equal(1, settled.Wallet.LegacyFreeGamesRemaining);
        Assert.Equal($"{round.Outcome.SpinId:N}-free-game-use", settled.Wallet.LegacyFreeGameTransactionId);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, key => key.EndsWith("-free-game-use", StringComparison.Ordinal));
        AssertWitness(report, $"{round.Outcome.SpinId:N}-free-games-award", "freeGames:ocean-odyssey-v1", 6, 7);

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var summary = report.Round.Audit.Summary;
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(OceanOdysseySpinEvidence.InspectNative(summary, data));
        var old = JsonNode.Parse(data.GetRawText())!.AsObject();
        old.Remove("oceanOdysseyDraws");
        old.Remove("oceanOdysseyProgress");
        old.Remove("engineOutcomeSha256");
        old.Remove("walletSettlementId");
        Assert.False(OceanOdysseySpinEvidence.IsNative(JsonSerializer.SerializeToElement(old)));
    }

    private static OceanOdysseyRoundResult Settle(OceanOdysseyPreparedRound round)
    {
        const long before = 50_000;
        var spend = before - round.WagerCents;
        var spin = round.Outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(OceanOdysseyEngine.GameId, Account, spin),
            round.WagerCents > 0 ? $"{spin}-wager" : null, round.PayoutCents > 0 ? $"{spin}-payout" : null,
            before, spend, spend + round.PayoutCents, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGameTransactionId: round.SpendsLegacy ? $"{spin}-free-game-use" : null,
            LegacyFreeGamesRemaining: round.Progress.After.LegacyRemaining);
        return new(OceanOdysseyRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        {
            Round = round,
            ChargedWagerCents = round.WagerCents
        };
    }

    private static void AssertWitness(ClassicSlotPaidReport report, string id, string currency, long amount, long after)
    {
        var witness = report.GameplayWitnesses[id];
        Assert.Equal(id, witness["transactionId"]);
        Assert.Equal(id, witness["idempotencyKey"]);
        Assert.Equal(currency, witness["currencyId"]);
        Assert.Equal(amount, witness["amount"]);
        Assert.Equal(after, witness["balanceAfter"]);
    }

    private static void AssertMismatch(RoundAuditSummary summary, JsonObject data) =>
        Assert.Equal("slot-ocean-odyssey-native-proof-mismatch", Assert.Single(OceanOdysseySpinEvidence.InspectNative(
            summary, JsonSerializer.SerializeToElement(data))).Code);

    private static long Multiply(long points) => checked((long)Math.Round(points * 1.5m, MidpointRounding.AwayFromZero));

    private sealed class ScriptedDraws(int[] stops) : IGameRandomSource
    {
        private int position;
        private bool patternRolled;

        public int Next(int maximumExclusive)
        {
            if (position < stops.Length) return stops[position++];
            if (maximumExclusive != 100 || patternRolled) return 0;
            patternRolled = true;
            return 99;
        }
    }
}
