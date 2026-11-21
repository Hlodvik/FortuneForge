using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.MidnightVault.Server;
using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.MidnightVault.Tests;

public sealed class MidnightVaultSpinReportingTests
{
    private const string Account = "midnight-report-compatibility";

    [Theory]
    [InlineData(MidnightVaultSlotDefinitions.LegacyPaytableId, 7)]
    [InlineData(MidnightVaultSlotDefinitions.PreviousPaytableId, 10)]
    [InlineData(MidnightVaultSlotDefinitions.PaytableId, 10)]
    public void HistoricalModelHeadKeepsItsExactOddWagerAndLegacyWalletSource(string model, int totalSpins)
    {
        var head = new MidnightVaultBonusRound("original-imported-head", totalSpins, 2, 101,
            MidnightVaultSlotDefinitions.CollectionFeatureMode, model);
        var queued = new MidnightVaultBonusRound("next-native-tail", 10, 10, 222,
            null, MidnightVaultSlotDefinitions.PaytableId);
        var before = new MidnightVaultRoundBefore([head, queued], 2, 9, 17, 2, Empty(), Empty());
        var request = new MidnightVaultRoundRequest("midnight-imported-head", head.WagerPoints, true, UseSpecialBoost: true);
        var started = DateTime.UtcNow;
        var round = MidnightVaultRoundPolicy.Prepare(Account, request, before, started, null, new SeededDraws());
        var settled = Settle(round);
        var completed = round.Outcome.EngineAudit.GeneratedAtUtc;
        var view = MidnightVaultSpinResponse.From(settled);
        var report = MidnightVaultSpinReport.Capture(round, settled, completed);

        Assert.Equal(model, view.PaytableId);
        Assert.Equal(101, view.WagerPoints);
        Assert.Equal(1, view.FreeSpinsRemaining);
        Assert.Equal(101, view.FreeSpinWagerPoints);
        Assert.Equal(0, view.EnergyBalance);
        Assert.False(view.EnergyMultiplierApplied);
        Assert.Equal(head with { RemainingSpins = 1 }, view.BonusRound);
        Assert.Equal(queued, view.QueuedBonusRounds[0]);
        Assert.Equal(MidnightVaultSlotDefinitions.CollectionFeatureMode, view.FreeSpinFeatureMode);
        Assert.All(view.Reels, reel => Assert.Equal(6, reel.Count));
        Assert.True(view.IsFreeSpin);
        Assert.Equal(1, settled.Wallet.LegacyFreeGamesRemaining);
        Assert.Equal(0d, report.Result["wageredSlotsCredits"]);
        Assert.Equal(model, report.Result["mathModelId"]);
        Assert.Equal((double)(view.Payout.TotalPoints * 25m / 100m), report.Result["wonSlotsCredits"]);
        Assert.False(report.GameplayWitnesses.ContainsKey($"{round.AdmissionId:N}-free-game-use"));
        var boost = report.GameplayWitnesses[$"{round.AdmissionId:N}-special-boost-use"];
        Assert.Equal("specialPoints:dragon-hoard-v1", boost["currencyId"]);
        Assert.Equal(-8L, boost["amount"]);
        Assert.Equal(1L, boost["balanceAfter"]);

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.False(data.GetProperty("result").TryGetProperty("chargedWagerCents", out _));
        var summary = report.Round.Audit.Summary;
        Assert.Equal("dragon-hoard", summary.GameId);
        var participant = Assert.Single(summary.Participants);
        Assert.Equal(0, participant.WagerCents);
        Assert.Equal(view.Payout.TotalPoints * 25, participant.PayoutCents);
        Assert.Empty(MidnightVaultSpinEvidence.InspectNative(summary, data));
        Assert.Empty(MidnightVaultSpinEvidence.InspectNative(summary with
        {
            StartedAtUtc = WithoutSubMicrosecond(started), CompletedAtUtc = WithoutSubMicrosecond(completed)
        }, data));

        var altered = JsonNode.Parse(data.GetRawText())!.AsObject();
        altered["midnightProgress"]!["walletLegacyRemaining"] = 2;
        AssertMismatch(summary, altered);
        altered["midnightProgress"]!["walletLegacyRemaining"] = 1;
        altered["midnightProgress"]!["spendsLegacy"] = false;
        AssertMismatch(summary, altered);
        altered["midnightProgress"]!["spendsLegacy"] = true;
        altered["midnightBonusEvidence"]!["roundsAfterConsumption"]![0]!["wagerPoints"] = 102;
        AssertMismatch(summary, altered);

        var oldEnvelope = JsonNode.Parse(data.GetRawText())!.AsObject();
        oldEnvelope.Remove("midnightProgress");
        oldEnvelope.Remove("engineOutcomeSha256");
        oldEnvelope.Remove("walletSettlementId");
        Assert.Empty(MidnightVaultSpinEvidence.InspectNative(summary, JsonSerializer.SerializeToElement(oldEnvelope)));
    }

    [Fact]
    public void NewlyEarnedQueueAppendsBehindNativeHeadAndReportsExactCollectionAward()
    {
        var model = MidnightVaultSlotDefinitions.PaytableId;
        var head = new MidnightVaultBonusRound("native-head", 10, 1, 3, null, model);
        var queued = new MidnightVaultBonusRound("native-tail", 10, 10, 4, null, model);
        var before = new MidnightVaultRoundBefore([head, queued], 0, 0, 0, 2,
            new Dictionary<string, long> { ["sync"] = 29 }, new Dictionary<string, long> { ["sync"] = 87 });
        var request = new MidnightVaultRoundRequest("midnight-native-reported", 3, true);
        var draws = new SeededDraws();
        var round = Enumerable.Range(0, 128)
            .Select(_ => MidnightVaultRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null, draws))
            .First(sample => sample.Progress.CollectionFreeSpinsAwarded == 10);
        var settled = Settle(round);
        var response = MidnightVaultSpinResponse.From(settled);
        var report = MidnightVaultSpinReport.Capture(round, settled, round.Outcome.EngineAudit.GeneratedAtUtc);
        var collection = Assert.Single(response.AwardedBonusRounds,
            bonus => bonus.Id.Contains(":collection:sync:", StringComparison.Ordinal));

        Assert.Equal(10, collection.TotalSpins);
        Assert.Equal(3, collection.WagerPoints);
        Assert.Equal(queued, response.BonusRound);
        Assert.Equal(4, response.FreeSpinWagerPoints);
        Assert.Contains(collection, response.QueuedBonusRounds);
        Assert.Equal(0L, settled.Wallet.LegacyFreeGamesRemaining);
        Assert.Null(settled.Wallet.LegacyFreeGameTransactionId);
        Assert.Equal(10, report.Result["sealFreeSpinsAwarded"]);
        var use = report.GameplayWitnesses[$"{round.AdmissionId:N}-free-game-use"];
        Assert.Equal("freeGames:dragon-hoard-v1", use["currencyId"]);
        Assert.Equal(-1L, use["amount"]);
        Assert.Equal(10L, use["balanceAfter"]);
        var award = report.GameplayWitnesses[$"{round.Outcome.SpinId:N}-free-games-award"];
        Assert.Equal((long)response.AwardedBonusRounds.Sum(bonus => bonus.TotalSpins), award["amount"]);
        Assert.Equal(round.Progress.After.Rounds.Sum(bonus => (long)bonus.RemainingSpins), award["balanceAfter"]);

        using var frozen = JsonDocument.Parse((string)report.Result["midnightSettlementJson"]);
        Assert.Equal(10, frozen.RootElement.GetProperty("SealFreeSpinsAwarded").GetInt32());
        Assert.Equal(response.AwardedBonusRounds, frozen.RootElement.GetProperty("AwardedBonusRounds")
            .Deserialize<MidnightVaultBonusRound[]>());
        Assert.Equal(queued, frozen.RootElement.GetProperty("BonusRound").Deserialize<MidnightVaultBonusRound>());
    }

    [Fact]
    public void CashWagerIsOmittedFromPublicBriefButBoundToTheSavedCentReceipt()
    {
        var before = new MidnightVaultRoundBefore([], 0, 0, 0, 0, Empty(), Empty());
        var request = new MidnightVaultRoundRequest("midnight-cash-reporting", 100, false);
        var stops = MidnightVaultBaseReelSet.CreateCurrent().Reels.Select(reel => reel.IndexOf("8")).ToArray();
        var round = MidnightVaultRoundPolicy.Prepare(Account, request, before, DateTime.UtcNow, null, new SeededDraws(stops));
        var settled = Settle(round);
        var report = MidnightVaultSpinReport.Capture(round, settled, round.Outcome.EngineAudit.GeneratedAtUtc);
        var publicJson = JsonSerializer.SerializeToElement(MidnightVaultSpinResponse.From(settled), RoundAuditCapture.Json);
        Assert.False(publicJson.TryGetProperty("chargedWagerCents", out _));
        Assert.True(round.PayoutCents > 0);
        Assert.Equal(round.Outcome.Payout.TotalPoints * 25, round.PayoutCents);
        Assert.Equal(25d, report.Result["wageredSlotsCredits"]);

        using var proof = JsonDocument.Parse(report.Round.Audit.PayloadJson);
        var data = proof.RootElement.GetProperty("data");
        Assert.Equal(2_500, data.GetProperty("result").GetProperty("chargedWagerCents").GetInt64());
        Assert.Empty(MidnightVaultSpinEvidence.InspectNative(report.Round.Audit.Summary, data));
        var tampered = JsonNode.Parse(data.GetRawText())!.AsObject();
        tampered["walletSettlementId"] = "unrelated-wallet-receipt";
        AssertMismatch(report.Round.Audit.Summary, tampered);
    }

    private static MidnightVaultRoundResult Settle(MidnightVaultPreparedRound round)
    {
        const long before = 5_000;
        var spend = before - round.WagerCents;
        var spin = round.Outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(MidnightVaultEngine.GameId, Account, spin),
            round.WagerCents > 0 ? $"{spin}-wager" : null, round.PayoutCents > 0 ? $"{spin}-payout" : null,
            before, spend, spend + round.PayoutCents, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGameTransactionId: round.SpendsLegacy ? $"{spin}-free-game-use" : null,
            LegacyFreeGamesRemaining: round.Progress.After.LegacyRemaining);
        return new(MidnightVaultRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        {
            Round = round,
            ChargedWagerCents = round.WagerCents
        };
    }

    private static void AssertMismatch(RoundAuditSummary summary, JsonObject data) =>
        Assert.Equal("slot-midnight-native-proof-mismatch", Assert.Single(MidnightVaultSpinEvidence.InspectNative(
            summary, JsonSerializer.SerializeToElement(data))).Code);

    private static DateTime WithoutSubMicrosecond(DateTime time) =>
        new(time.Ticks - time.Ticks % TimeSpan.TicksPerMicrosecond, time.Kind);

    private static IReadOnlyDictionary<string, long> Empty() => new Dictionary<string, long>();

    private sealed class SeededDraws(int[]? stops = null) : IGameRandomSource
    {
        private readonly Random fallback = new(6103);
        private int cursor;
        public int Next(int maximumExclusive) => stops is not null && cursor < stops.Length
            ? stops[cursor++] : fallback.Next(maximumExclusive);
    }
}
