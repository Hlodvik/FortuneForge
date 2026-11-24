using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.FrontierFortune.LocalHarness;
using FortuneForge.Games.FrontierFortune.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.FrontierFortune.Tests;

public sealed class OutlawHandTests
{
    private static readonly string RoundId = "fedcba9876543210fedcba9876543210";
    private static FrontierCommand Command(string kind, long revision = 0, string? roundId = null, bool[]? held = null, int?[]? rows = null, long wager = 0) =>
        new(Guid.NewGuid().ToString("N"), kind, wager, ExpectedRevision: kind == "spin" ? null : revision)
        { MathVersion = OutlawRules.EngineVersion, RoundId = roundId, Held = held, SelectedRows = rows };
    private static readonly string[][] JGrid = Enumerable.Range(0, 5).Select(_ => new[] { "J", "Q", "K" }).ToArray();

    [Theory]
    [InlineData("J,J,Q,K,A", "pair", 3)]
    [InlineData("J,J,Q,Q,A", "two-pair", 8)]
    [InlineData("J,J,J,K,A", "trips", 15)]
    [InlineData("J,J,J,Q,Q", "full-house", 25)]
    [InlineData("J,J,J,J,A", "four", 40)]
    [InlineData("J,J,J,J,WILD", "five", 60)]
    [InlineData("WILD,WILD,WILD,WILD,WILD", "grand", 250)]
    [InlineData("J,J,Q,Q,WILD", "full-house", 25)]
    public void FinalRanksUseBestWildSubstitutionAndActualWildGrand(string cards, string category, int multiplier)
    {
        var award = OutlawHand.Classify(cards.Split(','));
        Assert.Equal(category, award.Category); Assert.Equal(multiplier, award.Multiplier);
    }

    [Fact]
    public void ThreeDealsRetainOnlyHeldHandCardsAndFinalConfirmationAlonePays()
    {
        var hand = OutlawHand.Start(RoundId, 4);
        Assert.Equal(2, hand.DealsRemaining);
        hand = OutlawHand.Confirm(hand, JGrid, [0, 0, 0, 1, 2]);
        Assert.Equal("hold", hand.Phase); Assert.Equal(0, hand.AwardPoints);
        hand = OutlawHand.Deal(hand, [true, true, true, false, false]);
        Assert.Throws<ArgumentException>(() => OutlawHand.Confirm(hand, JGrid, [0, null, null, 0, 0]));
        hand = OutlawHand.Confirm(hand, JGrid, [null, null, null, 0, 0]);
        hand = OutlawHand.Deal(hand, [true, true, true, true, true]);
        hand = OutlawHand.Confirm(hand, JGrid, [null, null, null, null, null]);
        Assert.Equal("complete", hand.Phase); Assert.Equal(0, hand.DealsRemaining);
        Assert.Equal("five", hand.Category); Assert.Equal(240, hand.AwardPoints);
        Assert.Throws<ArgumentException>(() => OutlawHand.Deal(hand, [true, true, true, true, true]));
        Assert.Throws<ArgumentException>(() => OutlawHand.Confirm(hand, JGrid, [null, null, null, null, null]));
    }

    [Fact]
    public void AntePreservesOverflowAndUsesOnlyOldestTwelvePaidChipWagers()
    {
        var state = FrontierAccountState.Empty() with { Outlaw = new(2, Enumerable.Repeat(4L, 11).ToArray(), null) };
        var spin = FrontierRoundPolicy.Prepare("ante-player", Command("spin", wager: 8), state, new PaidPatternRandom(), DateTime.UtcNow);
        Assert.Equal(13, spin.After.Outlaw!.View().Chips);
        Assert.Equal(60, spin.After.Outlaw.View().WagerPointsTotal);
        Assert.Throws<FrontierConflictException>(() => FrontierRoundPolicy.Prepare("ante-player", Command("spin", wager: 4), spin.After, new PaidPatternRandom(), DateTime.UtcNow));
        var deal = FrontierRoundPolicy.Prepare("ante-player", Command("deal-in", spin.After.Revision), spin.After, new PaidPatternRandom(), DateTime.UtcNow);
        Assert.Equal(4, deal.After.Outlaw!.HandRound!.WagerPoints);
        Assert.Equal(new[] { 8L }, deal.After.Outlaw.ChipWagers);
        Assert.Equal(0, deal.WagerCents); Assert.Equal(0, deal.PayoutCents); Assert.Empty(deal.After.Wins);
        FrontierRoundPolicy.Validate(deal);
    }

    [Fact]
    public void MixedPaidStakesFundAverageRoundedDownToLegalWager()
    {
        var ante = new OutlawAccountState(2, new long[] { 4, 4, 8, 8, 12, 12, 16, 16, 20, 20, 24, 24 }, null);
        Assert.True(ante.View().Ready); Assert.Equal(12, ante.View().FundedWagerPoints);
    }

    [Fact]
    public void ArchivedV1RecordRetainsItsOriginalSerializedDigestAndReplay()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "frontier-v1-frozen-round.json"));
        var record = JsonSerializer.Deserialize<FrontierStoredRound>(json, FrontierRules.Json)!;
        Assert.Null(record.Prepared.Outlaw);
        FrontierRoundPolicy.Validate(record.Prepared);
        Assert.Equal(record.Prepared.OutcomeSha256, FrontierRoundPolicy.Digest(record.Prepared));
        Assert.DoesNotContain("mathVersion", JsonSerializer.Serialize(record.Prepared.Command, FrontierRules.Json));
        Assert.DoesNotContain("outlaw", JsonSerializer.Serialize(record.Prepared, FrontierRules.Json));
    }

    [Fact]
    public void V2NeverDiscardsAnActiveV1EarnedObligation()
    {
        var state = FrontierAccountState.Empty() with { Bonus = FrontierEngineTests.Board() };
        var error = Assert.Throws<FrontierConflictException>(() => FrontierRoundPolicy.Prepare("legacy-player", Command("spin", wager: 4), state, new PaidPatternRandom(), DateTime.UtcNow));
        Assert.Equal("frontier-legacy-obligation", error.Code);
        Assert.Equal(6, state.Bonus!.ShotsRemaining);
    }

    [FrontierFirestoreFact]
    public async Task ProductionStoreSettlesSixPaidSpinsAndThreeIncludedHandDealsOnce()
    {
        var db = new FirestoreDbBuilder { ProjectId = "demo-frontier-paid-validation", EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        var account = "outlaw-test-" + Guid.NewGuid().ToString("N");
        var wallet = new FrontierHarnessWallet(db);
        var service = new FrontierRoundService(new FrontierRoundStore(db, TimeProvider.System), wallet, wallet, new PaidPatternRandom(), TimeProvider.System);
        FrontierReply reply = null!;
        for (var index = 0; index < 6; index++) reply = await service.ExecuteAsync(account, Command("spin", wager: 4), true, default);
        Assert.Equal(12, reply.Ante!.Chips); Assert.Equal(99_400, reply.BalanceCents); Assert.Null(reply.Bonus);
        await Assert.ThrowsAsync<FrontierConflictException>(() => service.ExecuteAsync(account, Command("spin", wager: 4), true, default));
        reply = await service.ExecuteAsync(account, Command("deal-in", reply.Revision), false, default);
        Assert.Equal(2, reply.HandRound!.DealsRemaining);
        FrontierCommand? last = null;
        for (var deal = 0; deal < 3; deal++)
        {
            if (deal > 0) reply = await service.ExecuteAsync(account, Command("deal", reply.Revision, reply.HandRound!.RoundId, [false, false, false, false, false]), false, default);
            Assert.Empty(reply.Wins); Assert.Equal(0, reply.LastWinPoints);
            last = Command("confirm", reply.Revision, reply.HandRound!.RoundId, rows: [0, 0, 0, 0, 0]);
            var prior = reply.Revision;
            if (deal == 2)
            {
                var uncertain = new FrontierRoundService(new FrontierRoundStore(db, TimeProvider.System), new LosePositiveWalletReply(wallet),
                    wallet, new PaidPatternRandom(), TimeProvider.System);
                await Assert.ThrowsAsync<GameWalletUnavailableException>(() => uncertain.ExecuteAsync(account, last, false, default));
                var pending = await service.ReadOutlawAsync(account, default);
                Assert.Equal("confirm", pending.PendingOperation!.Kind);
                Assert.Equal("choose", pending.HandRound!.Phase);
                Assert.Equal(105_400, pending.BalanceCents);
            }
            reply = await service.ExecuteAsync(account, last, false, default);
            Assert.Equal(prior + 1, reply.Revision);
            Assert.Equal(FrontierRoundIdentity.Hash(reply), FrontierRoundIdentity.Hash(await service.ExecuteAsync(account, last with { SelectedRows = [0, 0, 0, 0, 0] }, false, default)));
        }
        Assert.Equal("complete", reply.HandRound!.Phase); Assert.Equal(240, reply.LastWinPoints); Assert.Equal(105_400, reply.BalanceCents);
        var receipts = await db.Collection("frontierValidationWalletReceipts").WhereEqualTo("accountId", account).GetSnapshotAsync();
        Assert.Equal(600, receipts.Documents.Sum(x => x.GetValue<long>("wagerCents")));
        Assert.Equal(6000, receipts.Documents.Sum(x => x.GetValue<long>("payoutCents")));
        await Assert.ThrowsAsync<FrontierConflictException>(() => service.ExecuteAsync(account, last! with { OperationId = Guid.NewGuid().ToString("N"), ExpectedRevision = reply.Revision }, false, default));
        await VerifyReadBackEvidence(db, account, last!);
    }

    private static async Task VerifyReadBackEvidence(FirestoreDb db, string account, FrontierCommand command)
    {
        var saved = await new FrontierRoundStore(db, TimeProvider.System).FindAsync(account, command, default);
        var round = saved!.Prepared;
        var audit = Assert.Single((await db.Collection(RoundAuditDocuments.Summaries).WhereEqualTo("roundId", round.AdmissionId).GetSnapshotAsync()).Documents);
        var evidence = await db.Collection(RoundAuditDocuments.Evidence).Document(audit.Id).GetSnapshotAsync();
        var summary = new RoundAuditSummary(audit.Id, "high-noon-fortune", round.AdmissionId, round.AdmissionId, 1,
            audit.GetValue<Timestamp>("startedAtUtc").ToDateTime(), audit.GetValue<Timestamp>("completedAtUtc").ToDateTime(),
            "passed", [], [new(account, "Player", true, round.WagerCents, round.PayoutCents, round.PayoutCents - round.WagerCents)],
            true, null, "frontier-replay-v2", null, audit.GetValue<string>("evidenceSha256"), "frontier-fortune-v2");
        using var document = JsonDocument.Parse(evidence.GetValue<string>("payloadJson"));
        Assert.Empty(FrontierEvidence.Inspect(summary, document.RootElement));
        Assert.NotEmpty(FrontierEvidence.Inspect(summary with { Participants = [summary.Participants[0] with { PayoutCents = 1 }] }, document.RootElement));
    }

    internal sealed class PaidPatternRandom : IGameRandomSource
    { public int Next(int maximumExclusive) => maximumExclusive == 1000 ? 999 : 0; }
    private sealed class LosePositiveWalletReply(IGameWalletClient inner) : IGameWalletClient
    {
        public async Task<GameWalletSettlementReceipt> SettleAsync(string account, GameWalletSettlementRequest request, CancellationToken token)
        {
            var receipt = await inner.SettleAsync(account, request, token);
            if (request.PayoutCents > 0) throw new GameWalletUnavailableException();
            return receipt;
        }
    }
}
