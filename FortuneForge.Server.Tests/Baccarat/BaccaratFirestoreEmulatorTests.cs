using FortuneForge.Games.Baccarat;
using FortuneForge.Games.Cards;
using FortuneForge.Server.Cards.Baccarat;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;
using Xunit;

namespace FortuneForge.Server.Tests.Baccarat;

[Collection("Blackjack table Firestore emulator")]
public sealed class BaccaratFirestoreEmulatorTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    [Fact]
    public async Task PairWagers_CommitOneRoundAndLedgerAcrossConcurrentInstances()
    {
        var database = CreateDatabase();
        const string userId = "pair-player";
        await database.Collection("userBalances").Document($"{userId}_slotsCredits")
            .SetAsync(new Dictionary<string, object>
            {
                ["available"] = 100L,
                ["availableFractionalCents"] = 0L,
                ["version"] = 1L,
                ["updatedAt"] = Timestamp.FromDateTime(DateTime.UtcNow),
            });

        var first = new BaccaratService(new BaccaratFirestoreStore(database), TimeProvider.System, PairShoe);
        var second = new BaccaratService(new BaccaratFirestoreStore(database), TimeProvider.System, PairShoe);
        var request = new CreateBaccaratRoundRequest("banker", 0.25m, 0.25m, 0.50m);
        const string key = "baccarat_pair_emulator_0001";
        var settled = await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            (index % 2 == 0 ? first : second).StartAsync(userId, request, key, CancellationToken.None)));

        Assert.Single(settled.Select(round => round.RoundId).Distinct());
        Assert.All(settled, round =>
        {
            Assert.Equal(102.49m, round.Balance);
            Assert.Equal(1m, round.TotalStake);
            Assert.Equal(3.49m, round.CombinedTotalReturn);
            Assert.Equal("win", round.PlayerPairDisposition);
            Assert.Equal("loss", round.BankerPairDisposition);
        });
        var recovered = await second.GetAsync(userId, settled[0].RoundId, CancellationToken.None);
        Assert.Equal(3.49m, recovered.CombinedTotalReturn);
        Assert.Equal(0.25m, recovered.PlayerPairStake);
        Assert.Equal(0.50m, recovered.BankerPairStake);

        var balance = await database.Collection("userBalances").Document($"{userId}_slotsCredits").GetSnapshotAsync();
        Assert.Equal(102L, balance.GetValue<long>("available"));
        Assert.Equal(49L, balance.GetValue<long>("availableFractionalCents"));
        Assert.Single((await database.Collection("baccaratRounds").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("baccaratRoundEvents").GetSnapshotAsync()).Documents);
        var entries = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.GetValue<double>("amount") == -1.0);
        Assert.Contains(entries, entry => entry.GetValue<double>("amount") == 3.49);

        await Assert.ThrowsAsync<BaccaratRoundConflictException>(() => second.StartAsync(
            userId, request with { BankerPairStake = 0m }, key, CancellationToken.None));
        Assert.Equal(2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task LegacyRounds_ReadAndReplayWithoutSideBetFields()
    {
        var database = CreateDatabase();
        const string userId = "legacy-player";
        const string key = "baccarat_legacy_round_0001";
        var roundId = BaccaratFirestoreStore.CreateLookupKey($"{userId}\n{key}");
        var dealt = PuntoBancoRoundDealer.Deal(PairShoe());
        var settlement = PuntoBancoPaytable.Settle(BaccaratBetSide.Banker, 10m, dealt);
        await database.Collection("userBalances").Document($"{userId}_slotsCredits")
            .SetAsync(new Dictionary<string, object> { ["available"] = 100L, ["availableFractionalCents"] = 0L });
        await database.Collection("baccaratRounds").Document(roundId).SetAsync(new Dictionary<string, object>
        {
            ["roundId"] = roundId,
            ["userId"] = userId,
            ["betSide"] = "banker",
            ["stakeCents"] = 1000L,
            ["consumedCards"] = dealt.ConsumedCards.Select(CardCode.Format).ToArray(),
            ["outcome"] = "banker",
            ["endedOnNatural"] = true,
            ["disposition"] = "win",
            ["profitCents"] = 950L,
            ["totalReturnCents"] = 1950L,
            ["shoeCardsUsed"] = (long)dealt.CardsConsumed,
            ["shoeCardsRemaining"] = 416L - dealt.CardsConsumed,
            ["startIdempotencyKey"] = key,
            ["schemaVersion"] = 1L,
        });
        await database.Collection("balanceTransactions").Document($"baccarat-{roundId}-wager")
            .SetAsync(new Dictionary<string, object> { ["amount"] = -10.0 });
        await database.Collection("balanceTransactions").Document($"baccarat-{roundId}-payout")
            .SetAsync(new Dictionary<string, object> { ["amount"] = 19.5 });
        await database.Collection("baccaratRoundEvents").Document($"{roundId}-settled")
            .SetAsync(new Dictionary<string, object> { ["roundId"] = roundId });

        var service = new BaccaratService(new BaccaratFirestoreStore(database), TimeProvider.System, PairShoe);
        var restored = await service.GetAsync(userId, roundId, CancellationToken.None);
        var replay = await service.StartAsync(userId,
            new CreateBaccaratRoundRequest("banker", 10m), key, CancellationToken.None);

        Assert.Equal(roundId, restored.RoundId);
        Assert.Equal(roundId, replay.RoundId);
        Assert.Null(restored.PlayerPairDisposition);
        Assert.Null(restored.BankerPairDisposition);
        Assert.Equal(0m, restored.PlayerPairStake);
        Assert.Equal(0m, restored.BankerPairStake);
        Assert.Equal(10m, restored.TotalStake);
        Assert.Equal(settlement.TotalReturn, restored.CombinedTotalReturn);
        await Assert.ThrowsAsync<BaccaratRoundConflictException>(() => service.StartAsync(userId,
            new CreateBaccaratRoundRequest("banker", 10m, 0.25m), key, CancellationToken.None));
    }

    private FirestoreDb CreateDatabase() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-fortuneforge-baccarat-{Guid.NewGuid():N}",
        Endpoint = fixture.Endpoint,
        ChannelCredentials = ChannelCredentials.Insecure,
        EmulatorDetection = EmulatorDetection.None,
    }.Build();

    private static IReadOnlyList<PlayingCard> PairShoe()
    {
        var prefix = new[]
        {
            new PlayingCard(CardRank.Four, CardSuit.Clubs),
            new PlayingCard(CardRank.King, CardSuit.Clubs),
            new PlayingCard(CardRank.Four, CardSuit.Hearts),
            new PlayingCard(CardRank.Nine, CardSuit.Clubs),
        };
        var cards = Enumerable.Range(0, 8).SelectMany(_ => StandardDeck.Create()).ToList();
        foreach (var card in prefix) cards.Remove(card);
        return [.. prefix, .. cards];
    }
}
