using System.Text.Json.Nodes;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.Cards;

namespace FortuneForge.Games.AndarBahar.Tests;

public sealed class AndarBaharServerTests
{
    [Fact]
    public async Task DurablePractice_ConcurrentInstancesReplayOnceAndRestartRecoversWithoutRedealing()
    {
        var path = NewPath();
        var first = Service(new PracticeAndarBaharStore(path, 10_000), true);
        var second = Service(new PracticeAndarBaharStore(path, 10_000), false);
        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => first.StartAsync("player", new("andar", .50m), "andar_concurrent_0001")));
        Assert.Single(results.Select(result => result.RoundId).Distinct());
        Assert.All(results, result => { Assert.Equal(.45m, result.Profit); Assert.Equal(.95m, result.TotalReturn); Assert.Equal(100.45m, result.Balance); });
        var recovered = await second.StartAsync("player", new("andar", .50m), "andar_concurrent_0001");
        Assert.Equal(results[0].RoundId, recovered.RoundId);
        Assert.Equal("andar", recovered.Winner);
        Assert.Single(recovered.Deals);
        Assert.Equal(recovered.RoundId, (await second.LatestAsync("player"))!.RoundId);
        Assert.Equal(recovered.RoundId, (await second.GetAsync("player", recovered.RoundId)).RoundId);
        await Assert.ThrowsAsync<AndarBaharRoundNotFoundException>(() => second.GetAsync("other", recovered.RoundId));
        Assert.Null(await second.LatestAsync("other"));
        await Assert.ThrowsAsync<AndarBaharRoundConflictException>(() => second.StartAsync("player", new("bahar", .50m), "andar_concurrent_0001"));
        await Assert.ThrowsAsync<AndarBaharRoundConflictException>(() => second.StartAsync("player", new("andar", 1m), "andar_concurrent_0001"));
        Assert.Equal(10_045, await new PracticeAndarBaharStore(path).BalanceAsync("player", default));
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Single(json["Rounds"]!.AsObject());
        Assert.Equal(2, json["Ledger"]!.AsObject().Count);
        Assert.Single(json["SettledEvents"]!.AsObject());
        Assert.Equal(2, json["Rounds"]![recovered.RoundId]!["RevealedCards"]!.AsArray().Count);
    }
    [Fact]
    public async Task DurablePractice_DifferentStoreInstancesCannotOverdrawSingleStake()
    {
        var path = NewPath();
        async Task<bool> Place(int index)
        {
            try { await Service(new PracticeAndarBaharStore(path, 50), false).StartAsync("player", new("andar", .50m), $"andar_compete_{index:0000}"); return true; }
            catch (AndarBaharInsufficientCreditsException) { return false; }
        }
        var results = await Task.WhenAll(Enumerable.Range(1, 8).Select(Place));
        Assert.Single(results, value => value);
        Assert.Equal(0, await new PracticeAndarBaharStore(path).BalanceAsync("player", default));
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        Assert.Single(json["Rounds"]!.AsObject());
        Assert.Single(json["Ledger"]!.AsObject());
    }
    [Fact]
    public async Task DurablePractice_NewKeysSettleAndBalanceRemainsCurrentDuringOldReplay()
    {
        var path = NewPath();
        var service = Service(new PracticeAndarBaharStore(path, 1000), true);
        var first = await service.StartAsync("player", new("andar", 1m), "andar_first_round_0001");
        var next = await service.StartAsync("player", new("bahar", .50m), "andar_second_round_001");
        var replay = await service.StartAsync("player", new("andar", 1m), "andar_first_round_0001");
        Assert.Equal(first.RoundId, replay.RoundId);
        Assert.Equal(10.40m, replay.Balance);
        Assert.Equal(next.RoundId, (await service.LatestAsync("player"))!.RoundId);
        Assert.Equal(10m, AndarBaharMoney.ToRand(await new PracticeAndarBaharStore(path).BalanceAsync("other", default)));
    }
    [Fact]
    public async Task CorruptPracticeState_FailsClosedWithoutResetOrDebit()
    {
        var path = NewPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{broken");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(new PracticeAndarBaharStore(path), true).StartAsync("player", new("andar", 1), "andar_corruption_0001"));
        Assert.Equal("{broken", await File.ReadAllTextAsync(path));
    }
    [Theory]
    [InlineData("TotalReturnCents", 9999)]
    [InlineData("BalanceAfterSettlementCents", 9999)]
    [InlineData("StakeCents", -50)]
    public async Task PersistedSettlementTampering_IsRejected(string property, long value)
    {
        var path = NewPath();
        var service = Service(new PracticeAndarBaharStore(path), true);
        var round = await service.StartAsync("player", new("andar", .50m), "andar_corrupt_settle_01");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["Rounds"]![round.RoundId]![property] = value;
        await File.WriteAllTextAsync(path, json.ToJsonString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync("player", round.RoundId));
    }
    [Fact]
    public async Task DeletedPracticeLedger_FailsClosedOnReadAndReplay()
    {
        var path = NewPath();
        var service = Service(new PracticeAndarBaharStore(path), true);
        var round = await service.StartAsync("player", new("andar", .50m), "andar_deleted_ledger_01");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["Ledger"]!.AsObject().Remove($"{round.RoundId}-payout");
        await File.WriteAllTextAsync(path, json.ToJsonString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync("player", round.RoundId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("player", new("andar", .50m), "andar_deleted_ledger_01"));
    }
    [Theory]
    [InlineData("andar", 0)] [InlineData("andar", -.5)] [InlineData("andar", .25)] [InlineData("andar", 100.5)] [InlineData("tie", 1)] [InlineData("Andar", 1)]
    public async Task InvalidWagerNeverCreatesPracticeState(string side, double stake)
    {
        var path = NewPath();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(new PracticeAndarBaharStore(path), true).StartAsync("player", new(side, (decimal)stake), "andar_invalid_stake_01"));
        Assert.False(File.Exists(path));
    }
    [Theory]
    [InlineData("")] [InlineData("short")] [InlineData("andar/bahar-key-00001")] [InlineData("andar key spaces 0001")]
    public async Task InvalidIdempotencyKeyCannotDebit(string key)
    {
        var path = NewPath();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(new PracticeAndarBaharStore(path), true).StartAsync("player", new("andar", 1), key));
        Assert.False(File.Exists(path));
    }
    [Fact]
    public async Task CancelledRequestDoesNotSettle()
    {
        var path = NewPath();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(new PracticeAndarBaharStore(path), true).StartAsync("player", new("andar", 1), "andar_cancelled_00001", new CancellationToken(true)));
        Assert.False(File.Exists(path));
    }
    [Fact]
    public void CryptoDeck_HasExactlyEachCardAndResponseOnlyContainsDealtPrefix()
    {
        var decks = Enumerable.Range(0, 8).Select(_ => AndarBaharService.CreateShuffledDeck()).ToArray();
        Assert.All(decks, deck => { Assert.Equal(52, deck.Count); Assert.Equal(52, deck.Distinct().Count()); Assert.All(StandardDeck.Create(), card => Assert.Contains(card, deck)); });
        Assert.Contains(decks.Skip(1), deck => !deck.SequenceEqual(decks[0]));
    }
    internal static string NewPath() => Path.Combine(Path.GetTempPath(), "FortuneForge-AndarBahar-tests", Guid.NewGuid().ToString("N"), "practice.json");
    internal static AndarBaharService Service(IAndarBaharStore store, bool andarWins) => new(store, TimeProvider.System, () => Deck(andarWins));
    internal static PlayingCard[] Deck(bool andarWins)
    {
        var all = StandardDeck.Create();
        var joker = new PlayingCard(CardRank.Ace, CardSuit.Spades);
        var match = new PlayingCard(CardRank.Ace, CardSuit.Hearts);
        var other = new PlayingCard(CardRank.King, CardSuit.Clubs);
        PlayingCard[] prefix = andarWins ? [joker, match] : [joker, other, match];
        return prefix.Concat(all.Where(card => !prefix.Contains(card))).ToArray();
    }
}
