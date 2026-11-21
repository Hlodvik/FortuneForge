using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.PiratesFortune.Server;
using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.PiratesFortune.Tests;

public sealed class PiratesSpinReportingTests
{
    private const string Account = "pirates-report-compatibility";

    [Fact]
    public void ScatterAndTwoGemGrantsKeepCompositeModeDirectValuesAndOneLegacyDebit()
    {
        var before = new PiratesRoundBefore(0, 2, new(2, 101, null), 0, 81, 0,
            new Dictionary<string, long> { ["sync"] = 24, ["rows"] = 24 },
            new Dictionary<string, long> { ["sync"] = 24 * 101L, ["rows"] = 24 * 101L });
        var round = Prepare(before, new("pirates-report-composite-award", 101, true));
        var settled = Settle(round);
        var spin = PiratesSpinResponse.From(settled);
        var report = PiratesSpinReport.Capture(round, settled, round.Outcome.Audit.GeneratedAtUtc);
        Assert.True(spin.IsFreeSpin);
        Assert.Equal(101, spin.WagerPoints);
        Assert.Equal(101, spin.FreeSpinWagerPoints);
        Assert.Equal(7, round.Outcome.FreeSpinsAwarded);
        Assert.Equal(27, spin.FreeSpinsAwarded);
        Assert.Equal(20, spin.CollectionFreeSpinsAwarded);
        Assert.Equal("sync-rows", spin.FreeSpinFeatureMode);
        Assert.Equal(28, spin.FreeSpinsRemaining);
        Assert.Equal(1, settled.Wallet.LegacyFreeGamesRemaining);
        Assert.Equal($"{round.Outcome.SpinId:N}-free-game-use", settled.Wallet.LegacyFreeGameTransactionId);
        Assert.DoesNotContain(report.GameplayWitnesses.Keys, key => key.EndsWith("-free-game-use", StringComparison.Ordinal));
        var award = report.GameplayWitnesses[$"{round.Outcome.SpinId:N}-free-games-award"];
        Assert.Equal("freeGames:pirates-fortune-v1", award["currencyId"]);
        Assert.Equal(27L, award["amount"]);
        Assert.Equal(28L, award["balanceAfter"]);
        Assert.Equal(7, report.Result["freeSpinsAwarded"]);
        Assert.Equal(20, report.Result["sealFreeSpinsAwarded"]);
        Assert.Equal("sync-rows", report.Result["sealFreeSpinFeatureMode"]);
        Assert.Equal("pirates-fortune-v1", report.Result["gameId"]);
        Assert.Equal("pirates-fortune", report.Round.Audit.Summary.GameId);
        Assert.Equal(0, spin.EnergyBalance);
        Assert.False(spin.EnergyMultiplierApplied);
        Assert.Equal(1m, spin.PayoutMultiplier);
        Assert.Equal(2, spin.MonkeyPawCount);
        Assert.Equal(134, spin.MoneyGrabPoints);
        Assert.Equal(spin.MoneyGrabPoints, Assert.Single(spin.Payout.Paylines, line => line.PaylineId == 901).AmountPoints);
        Assert.Equal(spin.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.Equal((double)(round.PayoutCents / 100m), report.Result["wonSlotsCredits"]);

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        var gems = data.GetProperty("piratesGemEvidence");
        Assert.Equal(PiratesGemBonus.RuleVersion, gems.GetProperty("ruleVersion").GetString());
        Assert.Equal(24 * 101L * 25, gems.GetProperty("wagerCentsBefore").GetProperty("sync").GetInt64());
        Assert.Equal(20, gems.GetProperty("freeSpinsAwarded").GetInt32());
        Assert.Equal("sync-rows", gems.GetProperty("featureMode").GetString());
        Assert.Equal(7, data.GetProperty("result").GetProperty("freeSpinsAwarded").GetInt32());
        Assert.Equal(81, data.GetProperty("engineAudit").GetProperty("energyBalanceBefore").GetInt64());
        Assert.Empty(PiratesSpinEvidence.InspectNative(report.Round.Audit.Summary, data));

        var historical = JsonNode.Parse(data.GetRawText())!.AsObject();
        historical.Remove("piratesDraws");
        historical.Remove("piratesProgress");
        historical.Remove("engineOutcomeSha256");
        historical.Remove("walletSettlementId");
        var old = JsonSerializer.SerializeToElement(historical);
        Assert.False(PiratesSpinEvidence.IsNative(old));
        Assert.Empty(PiratesSpinEvidence.InspectNative(report.Round.Audit.Summary, old));
    }

    [Fact]
    public void SeventhGemAwardUsesTheSavedSpinIdAndRejectsAnAlteredWeightedGrant()
    {
        var before = new PiratesRoundBefore(0, 0, null, 0, 0, 0,
            new Dictionary<string, long> { ["sync"] = 6 }, new Dictionary<string, long> { ["sync"] = 600 });
        var template = Prepare(before, new("pirates-seventh-gem-cash", 100, false));
        var miss = UseSpinId(template, new Guid("00000001-0000-0000-0000-000000000000"));
        var hit = UseSpinId(template, new Guid("00000005-0000-0000-0000-000000000000"));
        Assert.Empty(miss.Progress.GemAwards);
        Assert.Equal(7, miss.Progress.After.SealCounts["sync"]);
        var award = Assert.Single(hit.Progress.GemAwards);
        Assert.Equal(new PiratesGemGrant("sync", 7, 17_500), award);
        Assert.Equal(0, hit.Progress.After.SealCounts["sync"]);
        Assert.Equal(10, hit.Progress.CollectionFreeSpinsAwarded);

        var settled = Settle(hit);
        var report = PiratesSpinReport.Capture(hit, settled, hit.Outcome.Audit.GeneratedAtUtc);
        var summary = report.Round.Audit.Summary;
        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Empty(PiratesSpinEvidence.InspectNative(summary, data));
        Assert.Equal(2_500, data.GetProperty("result").GetProperty("chargedWagerCents").GetInt64());
        Assert.False(JsonSerializer.SerializeToElement(PiratesSpinResponse.From(settled), RoundAuditCapture.Json)
            .TryGetProperty("chargedWagerCents", out _));

        var altered = JsonNode.Parse(data.GetRawText())!.AsObject();
        altered["piratesProgress"]!["gemAwards"]![0]!["wagerTotalCents"] = 17_525;
        AssertMismatch(summary, altered);
        altered["piratesProgress"]!["gemAwards"]![0]!["wagerTotalCents"] = 17_500;
        altered["piratesGemEvidence"]!["wagerCentsBefore"]!["sync"] = 15_025;
        AssertMismatch(summary, altered);
    }

    private static PiratesPreparedRound Prepare(PiratesRoundBefore before, PiratesRoundRequest request)
    {
        var stops = PiratesFortuneEngine.CreateReelSet().Reels.Select(strip => strip.IndexOf("FREE")).ToArray();
        return PiratesRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null, new AwardDraws(stops));
    }

    private static PiratesPreparedRound UseSpinId(PiratesPreparedRound template, Guid spinId)
    {
        var outcome = template.Outcome with { SpinId = spinId };
        return template with
        {
            Engine = template.Engine with { Outcome = outcome },
            OutcomeSha256 = PiratesRoundPolicy.OutcomeDigest(outcome),
            Progress = PiratesRoundPolicy.Advance(template.Before, outcome, template.SpendsLegacy)
        };
    }

    private static PiratesRoundResult Settle(PiratesPreparedRound round)
    {
        const long before = 50_000;
        var spend = before - round.WagerCents;
        var spin = round.Outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(PiratesFortuneEngine.GameId, Account, spin),
            round.WagerCents > 0 ? $"{spin}-wager" : null, round.PayoutCents > 0 ? $"{spin}-payout" : null,
            before, spend, spend + round.PayoutCents, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGameTransactionId: round.SpendsLegacy ? $"{spin}-free-game-use" : null,
            LegacyFreeGamesRemaining: round.Progress.After.LegacyRemaining);
        return new(PiratesRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        {
            Round = round,
            ChargedWagerCents = round.WagerCents
        };
    }

    private static void AssertMismatch(RoundAuditSummary summary, JsonObject data) =>
        Assert.Equal("slot-pirates-native-proof-mismatch", Assert.Single(PiratesSpinEvidence.InspectNative(
            summary, JsonSerializer.SerializeToElement(data))).Code);

    private sealed class AwardDraws(int[] stops) : IGameRandomSource
    {
        private int reel;
        private bool directValueRolled;
        private bool choosingGems;
        private int gem;

        public int Next(int maximumExclusive)
        {
            if (reel < stops.Length) return stops[reel++];
            if (maximumExclusive == 100 && !directValueRolled)
            {
                directValueRolled = true;
                return 99;
            }
            if (maximumExclusive == 25) choosingGems = true;
            return choosingGems && maximumExclusive == 4 ? gem++ : 0;
        }
    }
}
