using System.Collections.Immutable;
using System.Text.Json;
using FortuneForge.Games.Mines.Server;

namespace FortuneForge.Games.Mines.Server.Tests;

public sealed class MinesStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ff-mines-tests-" + Guid.NewGuid().ToString("N"));
    private string LedgerPath => Path.Combine(directory, "practice.json");
    private const string Player = "test-player";
    private static string Key(int number) => $"mines-request-{number:00000000}";
    private MinesService Service(long balance = 100_000, int mines = 3) => new(new FileMinesPracticeStore(LedgerPath, balance), new MinesEngine(new FixedBoard(mines)));

    [Fact]
    public async Task FullFlow_RestartResumesSameBoardAndCashoutCreditsExactlyOnce()
    {
        var first = Service();
        var start = await first.StartAsync(Player, new(Key(1), 1_234, 3));
        var id = start.Round!.RoundId;
        Assert.Equal(98_766, start.BalanceCents);
        Assert.Equal("active", start.Round.Status);
        Assert.All(start.Round.Tiles, tile => Assert.Equal("hidden", tile.Kind));
        var reveal = await first.RevealAsync(Player, id, new(Key(2), 24));
        Assert.Equal(1, reveal.Round!.SafeReveals);
        var restarted = Service();
        Equal(reveal, await restarted.SessionAsync(Player));
        Equal(reveal, await restarted.RevealAsync(Player, id, new(Key(2), 24)));
        var paid = await restarted.CashOutAsync(Player, id, new(Key(3)));
        Assert.Equal(98_766 + MinesRules.ReturnCents(1_234, 3, 1), paid.BalanceCents);
        Assert.Equal("cashed-out", paid.Round!.Status);
        Assert.Equal(3, paid.Round.Tiles.Count(tile => tile.Kind == "mine"));
        Assert.Single(paid.Round.Tiles, tile => tile.IsRevealed);
        Equal(paid, await Service().CashOutAsync(Player, id, new(Key(3))));
        Equal(paid, await Service().SessionAsync(Player));
        await Assert.ThrowsAsync<MinesConflictException>(() => restarted.CashOutAsync(Player, id, new(Key(4))));
        var data = await Ledger();
        Assert.Equal(2, data.Ledger.Count);
        Assert.Equal(new[] { -1234L, MinesRules.ReturnCents(1234, 3, 1) }, data.Ledger.Select(entry => entry.AmountCents));
    }

    [Fact]
    public async Task ConcurrentInstances_DuplicateCommandsAreExactAndOneWagerOnly()
    {
        var first = Service(); var second = Service();
        var starts = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => (i % 2 == 0 ? first : second).StartAsync(Player, new(Key(1), 1_000, 3))));
        foreach (var result in starts) Equal(starts[0], result);
        var id = starts[0].Round!.RoundId;
        var reveals = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => (i % 2 == 0 ? first : second).RevealAsync(Player, id, new(Key(2), 24))));
        Assert.All(reveals, result => Assert.Equal(1, result.Round!.SafeReveals));
        var payouts = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => (i % 2 == 0 ? first : second).CashOutAsync(Player, id, new(Key(3)))));
        foreach (var result in payouts) Equal(payouts[0], result);
        Assert.Equal(2, (await Ledger()).Ledger.Count);
        Assert.Equal(100_125, (await first.SessionAsync(Player)).BalanceCents);
    }

    [Fact]
    public async Task DifferentConcurrentWagers_CannotOverwriteActiveRoundOrOverdraw()
    {
        async Task<bool> TryStart(int index)
        {
            try { await Service(100).StartAsync(Player, new(Key(index), 100, 3)); return true; }
            catch (MinesConflictException) { return false; }
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(TryStart));
        Assert.Single(results, won => won);
        Assert.Equal(0, (await Service(100).SessionAsync(Player)).BalanceCents);
        Assert.Single((await Ledger()).Rounds);
        Assert.Single((await Ledger()).Ledger);
    }

    [Fact]
    public async Task CashoutVersusMineRevealRace_HasOneTerminalResultAndOnePossiblePayout()
    {
        var service = Service();
        var id = (await service.StartAsync(Player, new(Key(1), 1_000, 3))).Round!.RoundId;
        await service.RevealAsync(Player, id, new(Key(2), 24));
        async Task<bool> Try(Func<Task<MinesSessionResponse>> action)
        {
            try { await action(); return true; } catch (MinesConflictException) { return false; }
        }
        var results = await Task.WhenAll(Try(() => Service().CashOutAsync(Player, id, new(Key(3)))), Try(() => Service().RevealAsync(Player, id, new(Key(4), 0))));
        Assert.Single(results, success => success);
        var final = await service.SessionAsync(Player);
        Assert.Contains(final.Round!.Status, new[] { "lost", "cashed-out" });
        Assert.Equal(final.Round.Status == "lost" ? 99_000 : 100_125, final.BalanceCents);
        Assert.Equal(final.Round.Status == "lost" ? 1 : 2, (await Ledger()).Ledger.Count);
    }

    [Fact]
    public async Task RequestId_BindsActionRoundAndPayloadDurably()
    {
        var service = Service();
        var original = new StartMinesRequest(Key(1), 100, 3);
        var id = (await service.StartAsync(Player, original)).Round!.RoundId;
        await Assert.ThrowsAsync<MinesConflictException>(() => Service().StartAsync(Player, original with { WagerCents = 101 }));
        await Assert.ThrowsAsync<MinesConflictException>(() => Service().StartAsync(Player, original with { MineCount = 4 }));
        await Assert.ThrowsAsync<MinesConflictException>(() => Service().RevealAsync(Player, id, new(Key(1), 24)));
        await service.RevealAsync(Player, id, new(Key(2), 24));
        await Assert.ThrowsAsync<MinesConflictException>(() => Service().RevealAsync(Player, id, new(Key(2), 23)));
        Assert.Equal(99_900, (await service.SessionAsync(Player)).BalanceCents);
        Assert.Single((await Ledger()).Ledger);
    }

    [Fact]
    public async Task Privacy_WrongPlayerCannotRevealOrCashoutOrRecoverAnotherPlayersRound()
    {
        var service = Service();
        var start = await service.StartAsync(Player, new(Key(1), 100, 3));
        var id = start.Round!.RoundId;
        await Assert.ThrowsAsync<MinesNotFoundException>(() => service.RevealAsync("attacker", id, new(Key(2), 24)));
        await Assert.ThrowsAsync<MinesNotFoundException>(() => service.CashOutAsync("attacker", id, new(Key(3))));
        Assert.Null((await service.SessionAsync("attacker")).Round);
        var json = JsonSerializer.Serialize(start);
        Assert.DoesNotContain("MinePositions", json);
        Assert.DoesNotContain("UserId", json);
        Assert.DoesNotContain("privateRound", json, StringComparison.OrdinalIgnoreCase);
        Assert.All(start.Round.Tiles, tile => Assert.False(tile.IsRevealed));
        Equal(start, await service.SessionAsync(Player));
    }

    [Fact]
    public async Task LossRevealsPredeterminedBoard_AndNewRoundResetsCleanly()
    {
        var service = Service();
        var id = (await service.StartAsync(Player, new(Key(1), 100, 3))).Round!.RoundId;
        await service.RevealAsync(Player, id, new(Key(2), 24));
        var loss = await service.RevealAsync(Player, id, new(Key(3), 0));
        Assert.Equal("lost", loss.Round!.Status);
        Assert.Equal(new[] { 0, 1, 2 }, loss.Round.Tiles.Where(tile => tile.Kind == "mine").Select(tile => tile.Index));
        Assert.Equal(99_900, loss.BalanceCents);
        await Assert.ThrowsAsync<MinesConflictException>(() => service.RevealAsync(Player, id, new(Key(4), 23)));
        var next = await service.StartAsync(Player, new(Key(5), 50, 1));
        Assert.NotEqual(id, next.Round!.RoundId);
        Assert.Equal(0, next.Round.SafeReveals);
        Assert.Equal(99_850, next.BalanceCents);
        Assert.All(next.Round.Tiles, tile => Assert.Equal("hidden", tile.Kind));
    }

    [Fact]
    public async Task MaximumWin_UsesExactInt64AndAutoCreditsOnce()
    {
        var service = Service(100_000, 12);
        var id = (await service.StartAsync(Player, new(Key(1), 10_000, 12))).Round!.RoundId;
        MinesSessionResponse? result = null;
        for (var tile = 12; tile < 25; tile++) result = await service.RevealAsync(Player, id, new(Key(tile), tile));
        Assert.Equal("cleared", result!.Round!.Status);
        Assert.Equal(51_482_970_000, result.Round.PayoutCents);
        Assert.Equal(51_483_060_000, result.BalanceCents);
        Equal(result, await Service().RevealAsync(Player, id, new(Key(24), 24)));
        await Assert.ThrowsAsync<MinesConflictException>(() => service.CashOutAsync(Player, id, new(Key(30))));
        Assert.Equal(2, (await Ledger()).Ledger.Count);
    }

    [Fact]
    public async Task HistoricalReceiptNeverReopensRound_AuthoritativeSessionRecoversAfterLostResponse()
    {
        var service = Service();
        var startRequest = new StartMinesRequest(Key(1), 100, 3);
        var original = await service.StartAsync(Player, startRequest);
        var id = original.Round!.RoundId;
        await service.RevealAsync(Player, id, new(Key(2), 24));
        // Simulate a response lost after the durable credit, followed by a process restart.
        _ = await service.CashOutAsync(Player, id, new(Key(3)));
        var recovered = await Service().CashOutAsync(Player, id, new(Key(3)));
        Assert.Equal("cashed-out", recovered.Round!.Status);
        Assert.Equal(100_012, recovered.BalanceCents);
        // Replaying an earlier start intentionally returns its original receipt, never mutates current state.
        Equal(original, await Service().StartAsync(Player, startRequest));
        Equal(recovered, await Service().SessionAsync(Player));
        var next = await Service().StartAsync(Player, new(Key(4), 50, 3));
        Equal(original, await Service().StartAsync(Player, startRequest));
        Equal(next, await Service().SessionAsync(Player));
        await Assert.ThrowsAsync<MinesConflictException>(() => Service().RevealAsync(Player, id, new(Key(5), 23)));
        Equal(next, await Service().SessionAsync(Player));
        Assert.Equal(3, (await Ledger()).Ledger.Count);
    }

    [Fact]
    public async Task InsufficientFundsAndInvalidActions_NeverDebitOrGenerateBoard()
    {
        var generator = new FixedBoard(3);
        var service = new MinesService(new FileMinesPracticeStore(LedgerPath, 10), new MinesEngine(generator));
        await Assert.ThrowsAsync<MinesInsufficientFundsException>(() => service.StartAsync(Player, new(Key(1), 11, 3)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync(Player, new(Key(1), 9, 3)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync(Player, new(Key(1), 10_001, 3)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync(Player, new(Key(1), 10, 25)));
        Assert.Equal(0, generator.Calls);
        Assert.False(File.Exists(LedgerPath));
        var id = (await service.StartAsync(Player, new(Key(1), 10, 3))).Round!.RoundId;
        await Assert.ThrowsAsync<MinesConflictException>(() => service.CashOutAsync(Player, id, new(Key(2))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RevealAsync(Player, id, new(Key(2), 25)));
        await service.RevealAsync(Player, id, new(Key(2), 24));
        await Assert.ThrowsAsync<MinesConflictException>(() => service.RevealAsync(Player, id, new(Key(3), 24)));
        Assert.Single((await Ledger()).Ledger);
    }

    private async Task<FileMinesPracticeStore.PracticeLedger> Ledger() => JsonSerializer.Deserialize<FileMinesPracticeStore.PracticeLedger>(await File.ReadAllTextAsync(LedgerPath))!;
    private static void Equal(MinesSessionResponse expected, MinesSessionResponse actual) => Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    internal sealed class FixedBoard(int count) : IMinesBoardGenerator
    {
        public int Calls { get; private set; }
        public ImmutableArray<int> Generate(int mineCount) { Calls++; return [.. Enumerable.Range(0, mineCount == count ? count : mineCount)]; }
    }
}
