using FortuneForge.Games.HouseHoldem;
using FortuneForge.Server.Cards.HouseHoldem;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.HouseHoldem;

[Collection("Blackjack table Firestore emulator")]
public sealed class HouseHoldemFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentRetriesDebitOnceAndSettlementPaysOnce()
    {
        var database = CreateDatabase();
        var first = new HouseHoldemFirestoreStore(database);
        var second = new HouseHoldemFirestoreStore(database);
        await SeedBalance(database, "alice", 10_000);
        var deck = HouseHoldemRules.CreateShuffledDeck();
        var startKey = Key("start");

        var starts = await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            (index % 2 == 0 ? first : second).StartAsync("alice", startKey, 100, 100, deck, Now, default)));
        Assert.Single(starts.Select(start => start.RoundId).Distinct());
        var roundId = starts[0].RoundId;
        Assert.Equal(9_700, await Balance(database, "alice"));
        Assert.Equal(2, starts[0].Round.PlayerCards.Count);
        Assert.Equal(roundId, await first.GetActiveRoundIdAsync("alice", default));
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<HouseHoldemRoundConflictException>(() => first.StartAsync(
            "alice", Key("different-start"), 100, 0, deck, Now, default));
        Assert.Null(await first.GetAsync("bob", roundId, default));
        await Assert.ThrowsAsync<HouseHoldemRoundNotFoundException>(() => first.TransitionAsync(
            "bob", roundId, Key("steal"), "play-4x", Now, default));

        var actionKey = Key("play-four");
        var actions = await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            (index % 2 == 0 ? first : second).TransitionAsync("alice", roundId, actionKey, "play-4x", Now, default)));
        Assert.All(actions, action => Assert.Equal("flop", action.Round.Phase));
        Assert.Equal(9_300, await Balance(database, "alice"));
        Assert.Equal(2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents.Count);
        await Assert.ThrowsAsync<HouseHoldemRoundConflictException>(() => second.TransitionAsync(
            "alice", roundId, actionKey, "check", Now, default));

        await first.TransitionAsync("alice", roundId, Key("reveal-river"), "advance", Now, default);
        var settled = await first.TransitionAsync("alice", roundId, Key("settle"), "advance", Now, default);
        Assert.Equal("settled", settled.Round.Phase);
        Assert.Null(await first.GetActiveRoundIdAsync("alice", default));
        Assert.Equal(9_300 + settled.Round.PayoutCents, await Balance(database, "alice"));
        var result = Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Equal("house-holdem", result.GetValue<string>("game"));
        Assert.Equal(settled.Round.TotalWagerCents, result.GetValue<long>("wagerCents"));
        Assert.Equal(settled.Round.PayoutCents, result.GetValue<long>("payoutCents"));
        var ledgerCount = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents.Count;
        Assert.Equal(settled.Round.PayoutCents > 0 ? 3 : 2, ledgerCount);

        var replay = await second.TransitionAsync("alice", roundId, Key("settle"), "advance", Now, default);
        Assert.Equal(settled.Round.PayoutCents, replay.Round.PayoutCents);
        Assert.Equal(9_300 + settled.Round.PayoutCents, await Balance(database, "alice"));
        Assert.Equal(ledgerCount, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents.Count);
        var next = await first.StartAsync("alice", Key("next-start"), 100, 0, deck, Now, default);
        Assert.NotEqual(roundId, next.RoundId);
        Assert.Equal(next.RoundId, await first.GetActiveRoundIdAsync("alice", default));
    }

    [Fact]
    public async Task ChecksAndFoldDoNotDebitAgainOrExposeOpponentBeforeSettlement()
    {
        var database = CreateDatabase();
        var store = new HouseHoldemFirestoreStore(database);
        await SeedBalance(database, "alice", 10_000);
        var start = await store.StartAsync("alice", Key("start"), 100, 0,
            HouseHoldemRules.CreateShuffledDeck(), Now, default);
        var preflop = HouseHoldemService.ToResponse(start);
        Assert.Null(preflop.DealerCards);
        Assert.Empty(preflop.Board);
        var flop = HouseHoldemService.ToResponse(await store.TransitionAsync("alice", start.RoundId,
            Key("check-one"), "check", Now, default));
        Assert.Null(flop.DealerCards);
        Assert.Equal(3, flop.Board.Count);
        var river = HouseHoldemService.ToResponse(await store.TransitionAsync("alice", start.RoundId,
            Key("check-two"), "check", Now, default));
        Assert.Null(river.DealerCards);
        Assert.Equal(5, river.Board.Count);
        Assert.Equal(9_800, await Balance(database, "alice"));
        var folded = HouseHoldemService.ToResponse(await store.TransitionAsync("alice", start.RoundId,
            Key("fold"), "fold", Now, default));
        Assert.Equal("fold", folded.Result);
        Assert.Equal(2, folded.DealerCards!.Count);
        Assert.Equal(9_800, await Balance(database, "alice"));
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<HouseHoldemRoundConflictException>(() => store.TransitionAsync(
            "alice", start.RoundId, Key("after-fold"), "advance", Now, default));
    }

    [Fact]
    public async Task StartRequiresFundsForMandatoryFinalPlayWager()
    {
        var database = CreateDatabase();
        var store = new HouseHoldemFirestoreStore(database);
        await SeedBalance(database, "alice", 200);

        await Assert.ThrowsAsync<HouseHoldemInsufficientCreditsException>(() => store.StartAsync(
            "alice", Key("start"), 100, 0, HouseHoldemRules.CreateShuffledDeck(), Now, default));

        Assert.Equal(200, await Balance(database, "alice"));
        Assert.Null(await store.GetActiveRoundIdAsync("alice", default));
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task InsufficientPlayFundsLeaveRoundAndWalletUntouched()
    {
        var database = CreateDatabase();
        var store = new HouseHoldemFirestoreStore(database);
        await SeedBalance(database, "alice", 300);
        var start = await store.StartAsync("alice", Key("start"), 100, 0,
            HouseHoldemRules.CreateShuffledDeck(), Now, default);
        await Assert.ThrowsAsync<HouseHoldemInsufficientCreditsException>(() => store.TransitionAsync(
            "alice", start.RoundId, Key("expensive-play"), "play-4x", Now, default));
        Assert.Equal(100, await Balance(database, "alice"));
        Assert.Equal("preflop", (await store.GetAsync("alice", start.RoundId, default))!.Round.Phase);
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    private FirestoreDb CreateDatabase() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-fortuneforge-house-holdem-{Guid.NewGuid():N}",
        Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
        EmulatorDetection = Google.Api.Gax.EmulatorDetection.None,
    }.Build();

    private static Task SeedBalance(FirestoreDb database, string userId, long cents) =>
        database.Collection("userBalances").Document($"{userId}_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["available"] = cents / 100,
            ["availableFractionalCents"] = cents % 100,
            ["version"] = 1L,
            ["updatedAt"] = Timestamp.FromDateTime(Now.UtcDateTime),
        });

    private static async Task<long> Balance(FirestoreDb database, string userId)
    {
        var snapshot = await database.Collection("userBalances").Document($"{userId}_slotsCredits").GetSnapshotAsync();
        return snapshot.GetValue<long>("available") * 100 + snapshot.GetValue<long>("availableFractionalCents");
    }

    private static string Key(string source) => source.PadRight(16, 'x');
}
