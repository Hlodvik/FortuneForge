using System.Text.Json;
using FortuneForge.Games.Cards;
using FortuneForge.Games.HiLo.Server;

namespace FortuneForge.Games.HiLo.Server.Tests;

public sealed class PracticeTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "hilo-tests-" + Guid.NewGuid().ToString("N"));
    private string PathName => Path.Combine(folder, "practice.json");
    private HiLoService Service(Func<PlayingCard> draw, long balance = 10000) => new(new DurablePracticeHiLoStore(PathName, balance), draw: draw);
    internal static PlayingCard Card(CardRank rank) => new(rank, CardSuit.Spades);
    private static string Key() => Guid.NewGuid().ToString("N");
    private static string Json(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public async Task Restart_RecoversUnfinishedRoundAndExactOperationResponses_AndDoesNotDrawOnReplay()
    {
        var draws = 0;
        var service = Service(() => { draws++; return Card(CardRank.Seven); });
        var start = new HiLoStartRequest(Key(), 100);
        var first = await service.StartAsync("alice", start);
        Assert.Equal(9900, first.WalletBalanceCents);
        Assert.Equal(0, first.Round!.Revision);
        var restarted = Service(() => throw new InvalidOperationException("No RNG on replay or resume"));
        Assert.Equal(Json(first), Json(await restarted.GetSessionAsync("alice")));
        Assert.Equal(Json(first), Json(await restarted.StartAsync("alice", start)));
        Assert.Equal(1, draws);
        Assert.Equal("operation_conflict", (await Assert.ThrowsAsync<HiLoException>(() => restarted.StartAsync("alice", start with { WagerCents = 200 }))).Code);
        var command = new HiLoActionRequest(Key(), 0, "cashout");
        var paid = await restarted.ActAsync("alice", first.Round.RoundId, command);
        Assert.Equal(10000, paid.WalletBalanceCents);
        Assert.Equal("cashedOut", paid.Round!.Status);
        Assert.Equal(Json(paid), Json(await Service(() => throw new InvalidOperationException()).ActAsync("alice", first.Round.RoundId, command)));
        Assert.Equal(Json(paid), Json(await restarted.GetSessionAsync("alice")));
    }

    [Fact]
    public async Task IndependentStoreInstances_ConcurrentStartAndGuess_AreSerializedAndRejectStaleCommands()
    {
        var draws = 0;
        PlayingCard Draw() { Interlocked.Increment(ref draws); return Card(CardRank.Seven); }
        var one = Service(Draw); var two = Service(Draw);
        async Task<HiLoSessionResponse?> Start(HiLoService svc)
        { try { return await svc.StartAsync("same", new(Key(), 100)); } catch (HiLoException e) when (e.Code == "round_active") { return null; } }
        var starts = await Task.WhenAll(Start(one), Start(two));
        var active = Assert.Single(starts, value => value is not null)!;
        Assert.Equal(1, draws);
        async Task<HiLoSessionResponse?> Guess(HiLoService svc)
        { try { return await svc.ActAsync("same", active.Round!.RoundId, new(Key(), 0, "higher")); } catch (HiLoException e) when (e.Code == "stale_revision") { return null; } }
        var guesses = await Task.WhenAll(Guess(one), Guess(two));
        var result = Assert.Single(guesses, value => value is not null)!;
        Assert.Equal("lost", result.Round!.Status); // Equal rank loses.
        Assert.Equal(2, draws);
        Assert.Equal(9900, result.WalletBalanceCents);
        Assert.Equal(1, result.Round.Revision);
        Assert.Equal(0, result.Round.CurrentReturnCents);
    }

    [Fact]
    public async Task InsufficientFundsAndImpossibleEdges_RejectBeforeDrawing_AndPrivatePlayersCannotSeeOrUseOtherRounds()
    {
        var draws = 0;
        var service = Service(() => { draws++; return Card(CardRank.Ace); }, 100);
        Assert.Equal("insufficient_funds", (await Assert.ThrowsAsync<HiLoException>(() => service.StartAsync("alice", new(Key(), 101)))).Code);
        Assert.Equal(0, draws);
        var started = await service.StartAsync("alice", new(Key(), 100));
        Assert.False(started.Round!.Lower.Available);
        Assert.Equal("invalid_choice", (await Assert.ThrowsAsync<HiLoException>(() => service.ActAsync("alice", started.Round.RoundId, new(Key(), 0, "lower")))).Code);
        Assert.Equal(1, draws);
        Assert.Null((await service.GetSessionAsync("bob")).Round);
        Assert.Equal("round_not_found", (await Assert.ThrowsAsync<HiLoException>(() => service.ActAsync("bob", started.Round.RoundId, new(Key(), 0, "cashout")))).Code);
        Assert.Equal(1, draws);
        var json = Json(await service.GetSessionAsync("alice"));
        Assert.DoesNotContain("\"seed\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"future\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"userId\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.Single(started.Round.History);
    }

    [Fact]
    public async Task WinningCashout_DebitsAndCreditsOnceAcrossConcurrentReplays_OffersMatchPaidReturn()
    {
        var cards = new Queue<PlayingCard>([Card(CardRank.Seven), Card(CardRank.King)]);
        var draws = 0;
        var service = Service(() => { draws++; return cards.Dequeue(); });
        var first = await service.StartAsync("player", new(Key(), 100));
        var guess = new HiLoActionRequest(Key(), 0, "higher");
        var won = await service.ActAsync("player", first.Round!.RoundId, guess);
        Assert.Equal(first.Round.Higher.ProjectedReturnCents, won.Round!.CurrentReturnCents);
        Assert.Equal(214, won.Round.CurrentReturnCents);
        var cashout = new HiLoActionRequest(Key(), 1, "cashout");
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Service(() => throw new InvalidOperationException()).ActAsync("player", first.Round.RoundId, cashout)));
        Assert.All(responses, value => Assert.Equal(10114, value.WalletBalanceCents));
        Assert.Equal(2, draws);
        var disk = await File.ReadAllTextAsync(PathName);
        using var document = JsonDocument.Parse(disk);
        var ledger = document.RootElement.GetProperty("Players").EnumerateObject().Single().Value.GetProperty("Ledger");
        Assert.Equal(2, ledger.GetArrayLength());
        Assert.Equal(-100, ledger[0].GetProperty("AmountCents").GetInt64());
        Assert.Equal(214, ledger[1].GetProperty("AmountCents").GetInt64());
    }

    [Fact]
    public async Task SkipLimit_NoAdditionalDrawOrMoneyChange_AndCapAutomaticallyCreditsOnce()
    {
        var draws = 0;
        var service = Service(() => { draws++; return Card(draws % 2 == 1 ? CardRank.Queen : CardRank.King); }, 100000);
        var state = await service.StartAsync("skip", new(Key(), 100));
        for (var index = 0; index < 52; index++) state = await service.ActAsync("skip", state.Round!.RoundId, new(Key(), state.Round.Revision, "skip"));
        Assert.False(state.Round!.CanSkip);
        Assert.Equal("invalid_choice", (await Assert.ThrowsAsync<HiLoException>(() => service.ActAsync("skip", state.Round.RoundId, new(Key(), state.Round.Revision, "skip")))).Code);
        Assert.Equal(53, draws);
        Assert.Equal(99900, state.WalletBalanceCents);
        state = await service.ActAsync("skip", state.Round.RoundId, new(Key(), 52, "cashout"));
        // Queen->King (12.87), King->Queen(1.0725), Queen->King(12.87) repeated reaches1000x cap.
        var cap = await service.StartAsync("cap", new(Key(), 1000));
        while (cap.Round!.Status == "active")
            cap = await service.ActAsync("cap", cap.Round.RoundId, new(Key(), cap.Round.Revision, cap.Round.CurrentCard.Rank == "queen" ? "higher" : "lower"));
        Assert.Equal("maximumReturn", cap.Round.Status);
        Assert.Equal(1000, cap.Round.Multiplier);
        Assert.Equal(1000000, cap.Round.CurrentReturnCents);
        Assert.Equal(1099000, cap.WalletBalanceCents);
        Assert.False(cap.Round.CanCashOut);
    }
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
