using FortuneForge.Games.Cards;
using FortuneForge.Games.HiLo.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Games.HiLo.Server.Tests;

[CollectionDefinition("Hi-Lo Firestore", DisableParallelization = true)]
public sealed class HiLoFirestoreCollection : ICollectionFixture<HiLoEmulator>;

[Collection("Hi-Lo Firestore")]
public sealed class FirestoreTests(HiLoEmulator emulator)
{
    private FirestoreDb Database() => new FirestoreDbBuilder { ProjectId = "demo-hilo-" + Guid.NewGuid().ToString("N"), Endpoint = emulator.Endpoint,
        ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None }.Build();
    private static Task Seed(FirestoreDb db, string user, long cents = 10000) => db.Collection("userBalances").Document(user + "_slotsCredits").SetAsync(new Dictionary<string, object>
    { ["available"] = cents / 100, ["availableFractionalCents"] = cents % 100, ["version"] = 1L, ["unrelatedWalletField"] = "preserve" });
    private static Task<DocumentSnapshot> Wallet(FirestoreDb db, string user) => db.Collection("userBalances").Document(user + "_slotsCredits").GetSnapshotAsync();
    private static HiLoService Service(FirestoreDb db, CardRank rank) => new(new HiLoFirestoreStore(db), draw: () => PracticeTests.Card(rank));
    private static string Key() => Guid.NewGuid().ToString("N");

    [Fact]
    public async Task ConcurrentDuplicates_AreOneDebitAndOneCredit_ResumeAndPayloadConflictsAreDurable()
    {
        var db = Database(); await Seed(db, "player");
        var service = Service(db, CardRank.Seven); var another = Service(db, CardRank.Seven);
        var start = new HiLoStartRequest(Key(), 100);
        // Two independent service instances still race the same operation without
        // saturating the Firestore emulator's transaction lock under the full suite.
        var starts = await Task.WhenAll(service.StartAsync("player", start), another.StartAsync("player", start));
        Assert.Single(starts.Select(value => value.Round!.RoundId).Distinct());
        Assert.All(starts, value => Assert.Equal(9900, value.WalletBalanceCents));
        Assert.Single((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Equal("operation_conflict", (await Assert.ThrowsAsync<HiLoException>(() => another.StartAsync("player", start with { WagerCents = 200 }))).Code);
        var round = starts[0].Round!;
        var won = await Service(db, CardRank.King).ActAsync("player", round.RoundId, new(Key(), 0, "higher"));
        Assert.Equal(214, won.Round!.CurrentReturnCents);
        var cashout = new HiLoActionRequest(Key(), 1, "cashout");
        var payouts = await Task.WhenAll(
            service.ActAsync("player", round.RoundId, cashout),
            another.ActAsync("player", round.RoundId, cashout));
        Assert.All(payouts, value => Assert.Equal(10114, value.WalletBalanceCents));
        var wallet = await Wallet(db, "player");
        Assert.Equal(101L, wallet.GetValue<long>("available")); Assert.Equal(14L, wallet.GetValue<long>("availableFractionalCents"));
        Assert.Equal(3L, wallet.GetValue<long>("version")); Assert.Equal("preserve", wallet.GetValue<string>("unrelatedWalletField"));
        Assert.Equal(2, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.Equal(3, (await db.Collection("hiLoOperations").GetSnapshotAsync()).Count);
        Assert.Equal(3, (await db.Collection("hiLoEvents").GetSnapshotAsync()).Count);
        var history = Assert.Single((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Equal("hi-lo-" + round.RoundId, history.Id);
        Assert.Equal("player", history.GetValue<string>("userId"));
        Assert.Equal("hi-lo", history.GetValue<string>("game"));
        Assert.Equal(100L, history.GetValue<long>("wagerCents"));
        Assert.Equal(214L, history.GetValue<long>("payoutCents"));
        Assert.Equal(114L, history.GetValue<long>("netCents"));
        Assert.False(history.ContainsField("roundJson"));
        Assert.False(history.ContainsField("history"));
        var seen = Timestamp.FromDateTime(new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc));
        await history.Reference.UpdateAsync("seenAt", seen);
        await another.ActAsync("player", round.RoundId, cashout);
        Assert.Equal(seen, (await history.Reference.GetSnapshotAsync()).GetValue<Timestamp>("seenAt"));
        var restored = await new HiLoService(new HiLoFirestoreStore(db), draw: () => throw new InvalidOperationException("No RNG on reads/replay")).GetSessionAsync("player");
        Assert.False(restored.Practice); Assert.Equal("cashedOut", restored.Round!.Status); Assert.Equal(10114, restored.WalletBalanceCents);
        Assert.Null((await another.GetSessionAsync("other")).Round);
        Assert.Equal("round_not_found", (await Assert.ThrowsAsync<HiLoException>(() => another.ActAsync("other", round.RoundId, new(Key(), 0, "cashout")))).Code);
    }

    [Fact]
    public async Task CompetingStartsAndActions_CannotOverdrawOrApplyTwoGuessesAtSameRevision()
    {
        var db = Database(); await Seed(db, "player", 100);
        async Task<HiLoSessionResponse?> Start()
        { try { return await Service(db, CardRank.Seven).StartAsync("player", new(Key(), 100)); } catch (HiLoException exception) when (exception.Code == "round_active") { return null; } }
        var starts = await Task.WhenAll(Start(), Start()); var round = Assert.Single(starts, value => value is not null)!.Round!;
        async Task<HiLoSessionResponse?> Guess()
        { try { return await Service(db, CardRank.King).ActAsync("player", round.RoundId, new(Key(), 0, "higher")); } catch (HiLoException exception) when (exception.Code == "stale_revision") { return null; } }
        var guesses = await Task.WhenAll(Guess(), Guess()); var result = Assert.Single(guesses, value => value is not null)!;
        Assert.Equal(1, result.Round!.Revision); Assert.Equal(1, result.Round.Streak); Assert.Equal(0, result.WalletBalanceCents);
        Assert.Single((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(2, (await db.Collection("hiLoOperations").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task InsufficientAndInvalidWallet_ProduceNoWritesOrRngDraws_EqualRanksLoseWithoutCredit()
    {
        var db = Database(); await Seed(db, "poor", 10);
        var draws = 0;
        var service = new HiLoService(new HiLoFirestoreStore(db), draw: () => { draws++; return PracticeTests.Card(CardRank.Seven); });
        Assert.Equal("insufficient_funds", (await Assert.ThrowsAsync<HiLoException>(() => service.StartAsync("poor", new(Key(), 11)))).Code);
        Assert.Equal(0, draws); Assert.Empty((await db.Collection("hiLoOperations").GetSnapshotAsync()).Documents);
        var start = await service.StartAsync("poor", new(Key(), 10));
        var loss = await service.ActAsync("poor", start.Round!.RoundId, new(Key(), 0, "higher"));
        Assert.Equal("lost", loss.Round!.Status); Assert.Equal(0, loss.Round.CurrentReturnCents);
        var lossHistory = Assert.Single((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Equal(0L, lossHistory.GetValue<long>("payoutCents"));
        Assert.Equal(-10L, lossHistory.GetValue<long>("netCents"));
        Assert.Single((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        await db.Collection("userBalances").Document("bad_slotsCredits").SetAsync(new Dictionary<string, object> { ["availableFractionalCents"] = 10L });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("bad", new(Key(), 10)));
        Assert.Equal(2, draws);
    }

    [Fact]
    public async Task AbsoluteCapAndFractionalRecurringMultiplier_PayExactlyOnce()
    {
        var db = Database(); await Seed(db, "cap", 100000);
        var service = Service(db, CardRank.Queen);
        var state = await service.StartAsync("cap", new(Key(), 30000));
        while (state.Round!.Status == "active")
        {
            var higher = state.Round.CurrentCard.Rank == "queen";
            state = await Service(db, higher ? CardRank.King : CardRank.Queen).ActAsync("cap", state.Round.RoundId, new(Key(), state.Round.Revision, higher ? "higher" : "lower"));
        }
        Assert.Equal("maximumReturn", state.Round.Status); Assert.Equal(10000000, state.Round.CurrentReturnCents);
        var cappedHistory = Assert.Single((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Equal(10000000L, cappedHistory.GetValue<long>("payoutCents"));
        Assert.Equal(10070000, state.WalletBalanceCents);
        Assert.Equal(2, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.Equal(100700L, (await Wallet(db, "cap")).GetValue<long>("available"));
    }

    [Fact]
    public async Task CorruptLedgerEventOrRevealedRound_FailsClosedWithoutAdditionalDebits()
    {
        var db = Database(); await Seed(db, "player"); var service = Service(db, CardRank.Seven);
        var start = new HiLoStartRequest(Key(), 100); var state = await service.StartAsync("player", start);
        var id = HiLoService.Hash("player\n" + start.OperationId);
        var ledger = db.Collection("balanceTransactions").Document("hi-lo-" + id);
        await ledger.UpdateAsync("amountCents", -999L);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("player", start));
        await ledger.UpdateAsync("amountCents", -100L);
        await db.Collection("hiLoEvents").Document(id).UpdateAsync("userId", "other");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("player", start));
        var head = db.Collection("hiLoPlayers").Document(HiLoService.Hash("player"));
        var saved = await head.GetSnapshotAsync();
        var roundJson = saved.GetValue<string>("roundJson").Replace("\"Multiplier\":1,", "\"Multiplier\":900,");
        await head.UpdateAsync("roundJson", roundJson);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetSessionAsync("player"));
        Assert.Equal(99L, (await Wallet(db, "player")).GetValue<long>("available"));
        Assert.Single((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task MaintenanceMode_PaidWalletCanRecoverCashoutAndReplay_WithoutAnyNewRiskOrRng()
    {
        var db = Database(); await Seed(db, "player");
        var start = new HiLoStartRequest(Key(), 100);
        var original = await Service(db, CardRank.Seven).StartAsync("player", start);
        var paused = new HiLoService(new HiLoFirestoreStore(db), draw: () => throw new InvalidOperationException("No RNG when paused"), allowNewMutations: false);
        Assert.Equal(original.Round!.RoundId, (await paused.StartAsync("player", start)).Round!.RoundId);
        Assert.Equal("hi_lo_disabled", (await Assert.ThrowsAsync<HiLoException>(() => paused.StartAsync("player", new(Key(), 100)))).Code);
        Assert.Equal("hi_lo_disabled", (await Assert.ThrowsAsync<HiLoException>(() => paused.ActAsync("player", original.Round.RoundId, new(Key(), 0, "higher")))).Code);
        var request = new HiLoActionRequest(Key(), 0, "cashout");
        var paid = await paused.ActAsync("player", original.Round.RoundId, request);
        Assert.Equal(10000, paid.WalletBalanceCents);
        Assert.False(paid.Enabled);
        var replay = await paused.ActAsync("player", original.Round.RoundId, request);
        Assert.Equal(10000, replay.WalletBalanceCents);
        Assert.False(replay.Enabled);
        Assert.Single((await db.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Equal(2, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }
}
