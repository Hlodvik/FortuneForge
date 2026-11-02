using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        var start = await first.StartAsync(Player, new(Key(1), 123, 3));
        var id = start.Round!.RoundId;
        Assert.Equal(99_877, start.BalanceCents);
        Assert.Equal(100, start.MinimumWagerCents);
        Assert.Equal(500, start.MaximumWagerCents);
        Assert.Equal(MinesRules.PayoutPolicyVersion, start.PayoutPolicyVersion);
        Assert.Equal(MinesRules.PayoutPolicyVersion, start.Round.PayoutPolicyVersion);
        Assert.Equal("active", start.Round.Status);
        Assert.Equal(MinesRules.HouseEdgeBasisPoints, start.HouseEdgeBasisPoints);
        Assert.Equal(MinesRules.HouseEdgeBasisPoints, start.Round.HouseEdgeBasisPoints);
        Assert.All(start.Round.Tiles, tile => Assert.Equal("hidden", tile.Kind));
        var reveal = await first.RevealAsync(Player, id, new(Key(2), 24));
        Assert.Equal(1, reveal.Round!.SafeReveals);
        var restarted = Service();
        Equal(reveal, await restarted.SessionAsync(Player));
        Equal(reveal, await restarted.RevealAsync(Player, id, new(Key(2), 24)));
        var paid = await restarted.CashOutAsync(Player, id, new(Key(3)));
        Assert.Equal(99_877 + MinesRules.ReturnCents(123, 3, 1), paid.BalanceCents);
        Assert.Equal("cashed-out", paid.Round!.Status);
        Assert.Equal(3, paid.Round.Tiles.Count(tile => tile.Kind == "mine"));
        Assert.Single(paid.Round.Tiles, tile => tile.IsRevealed);
        Equal(paid, await Service().CashOutAsync(Player, id, new(Key(3))));
        Equal(paid, await Service().SessionAsync(Player));
        await Assert.ThrowsAsync<MinesConflictException>(() => restarted.CashOutAsync(Player, id, new(Key(4))));
        var data = await Ledger();
        Assert.Equal(2, data.Ledger.Count);
        Assert.Equal(new[] { -123L, MinesRules.ReturnCents(123, 3, 1) }, data.Ledger.Select(entry => entry.AmountCents));
    }

    [Fact]
    public async Task ConcurrentInstances_DuplicateCommandsAreExactAndOneWagerOnly()
    {
        var first = Service(); var second = Service();
        var starts = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => (i % 2 == 0 ? first : second).StartAsync(Player, new(Key(1), 500, 3))));
        foreach (var result in starts) Equal(starts[0], result);
        var id = starts[0].Round!.RoundId;
        var reveals = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => (i % 2 == 0 ? first : second).RevealAsync(Player, id, new(Key(2), 24))));
        Assert.All(reveals, result => Assert.Equal(1, result.Round!.SafeReveals));
        var payouts = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => (i % 2 == 0 ? first : second).CashOutAsync(Player, id, new(Key(3)))));
        foreach (var result in payouts) Equal(payouts[0], result);
        Assert.Equal(2, (await Ledger()).Ledger.Count);
        Assert.Equal(100_011, (await first.SessionAsync(Player)).BalanceCents);
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
        var id = (await service.StartAsync(Player, new(Key(1), 500, 3))).Round!.RoundId;
        await service.RevealAsync(Player, id, new(Key(2), 24));
        async Task<bool> Try(Func<Task<MinesSessionResponse>> action)
        {
            try { await action(); return true; } catch (MinesConflictException) { return false; }
        }
        var results = await Task.WhenAll(Try(() => Service().CashOutAsync(Player, id, new(Key(3)))), Try(() => Service().RevealAsync(Player, id, new(Key(4), 0))));
        Assert.Single(results, success => success);
        var final = await service.SessionAsync(Player);
        Assert.Contains(final.Round!.Status, new[] { "lost", "cashed-out" });
        Assert.Equal(final.Round.Status == "lost" ? 99_500 : 100_011, final.BalanceCents);
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
        var next = await service.StartAsync(Player, new(Key(5), 100, 1));
        Assert.NotEqual(id, next.Round!.RoundId);
        Assert.Equal(0, next.Round.SafeReveals);
        Assert.Equal(99_800, next.BalanceCents);
        Assert.All(next.Round.Tiles, tile => Assert.Equal("hidden", tile.Kind));
    }

    [Fact]
    public async Task MaximumWin_UsesExactInt64AndAutoCreditsOnce()
    {
        var service = Service(100_000, 12);
        var id = (await service.StartAsync(Player, new(Key(1), 500, 12))).Round!.RoundId;
        MinesSessionResponse? result = null;
        for (var tile = 12; tile < 25; tile++) result = await service.RevealAsync(Player, id, new(Key(tile), tile));
        Assert.Equal("cleared", result!.Round!.Status);
        Assert.Equal(25_000, result.Round.PayoutCents);
        Assert.Equal(124_500, result.BalanceCents);
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
        Assert.Equal(100_002, recovered.BalanceCents);
        // Replaying an earlier start intentionally returns its original receipt, never mutates current state.
        Equal(original, await Service().StartAsync(Player, startRequest));
        Equal(recovered, await Service().SessionAsync(Player));
        var next = await Service().StartAsync(Player, new(Key(4), 100, 3));
        Equal(original, await Service().StartAsync(Player, startRequest));
        Equal(next, await Service().SessionAsync(Player));
        await Assert.ThrowsAsync<MinesConflictException>(() => Service().RevealAsync(Player, id, new(Key(5), 23)));
        Equal(next, await Service().SessionAsync(Player));
        Assert.Equal(3, (await Ledger()).Ledger.Count);
    }

    [Fact]
    public async Task LegacyJsonActiveRoundAndReceipts_RetainAcceptedTermsAcrossRestartAndNextWager()
    {
        var accepted = new MinesRoundState(100, 3, [0, 1, 2], [], MinesRoundStatus.Active, 0);
        var revealed = new MinesRoundState(100, 3, [0, 1, 2], [24], MinesRoundStatus.Active, 0);
        var now = DateTimeOffset.UtcNow;
        var id = MinesService.Hash($"{Player}\n{Key(1)}");
        var startReceipt = LegacySession(99_900, id, accepted);
        var revealReceipt = LegacySession(99_900, id, revealed);
        var ledger = new FileMinesPracticeStore.PracticeLedger
        {
            Balances = { [Player] = 99_900 }, Latest = { [Player] = id },
            Rounds = { [id] = new(id, Player, revealed, 2, now, now) },
            Commands =
            {
                [MinesService.Hash($"{Player}\n{Key(1)}")] = new(Player, "start:100:3", startReceipt),
                [MinesService.Hash($"{Player}\n{Key(2)}")] = new(Player, $"reveal:{id}:24", revealReceipt),
            },
            Ledger = { new(MinesService.Hash($"{Player}\n{Key(1)}"), Player, id, -100, 99_900, "mines-wager", now) },
        };
        Directory.CreateDirectory(directory);
        // This is the schema emitted before per-round terms existed: no edge metadata anywhere.
        await File.WriteAllTextAsync(LedgerPath, WithoutHouseEdge(ledger));

        var service = Service();
        var resumed = await service.SessionAsync(Player);
        Assert.Equal(MinesRules.HouseEdgeBasisPoints, resumed.HouseEdgeBasisPoints);
        Assert.Equal(MinesRules.LegacyHouseEdgeBasisPoints, resumed.Round!.HouseEdgeBasisPoints);
        Assert.Equal(MinesRules.LegacyPayoutPolicyVersion, resumed.Round.PayoutPolicyVersion);
        Assert.Equal(MinesRules.PayoutPolicyVersion, resumed.PayoutPolicyVersion);
        Assert.Equal(112, resumed.Round.CashOutCents);
        Equal(startReceipt, await service.StartAsync(Player, new(Key(1), 100, 3)));
        Equal(revealReceipt, await Service().RevealAsync(Player, id, new(Key(2), 24)));
        Equal(resumed, await Service().SessionAsync(Player));

        var paid = await Service().CashOutAsync(Player, id, new(Key(3)));
        Assert.Equal(100_012, paid.BalanceCents);
        Assert.Equal(112, paid.Round!.PayoutCents);
        Assert.Equal(MinesRules.HouseEdgeBasisPoints, paid.HouseEdgeBasisPoints);
        Assert.Equal(MinesRules.LegacyHouseEdgeBasisPoints, paid.Round.HouseEdgeBasisPoints);
        Equal(paid, await Service().SessionAsync(Player));
        Equal(paid, await Service().CashOutAsync(Player, id, new(Key(3))));

        var next = await Service().StartAsync(Player, new(Key(4), 100, 3));
        Assert.Equal(MinesRules.HouseEdgeBasisPoints, next.HouseEdgeBasisPoints);
        Assert.Equal(MinesRules.HouseEdgeBasisPoints, next.Round!.HouseEdgeBasisPoints);
        Equal(startReceipt, await Service().StartAsync(Player, new(Key(1), 100, 3)));
        Equal(revealReceipt, await Service().RevealAsync(Player, id, new(Key(2), 24)));
        Equal(paid, await Service().CashOutAsync(Player, id, new(Key(3))));
        Equal(next, await Service().SessionAsync(Player));
        Assert.Equal(new long[] { -100, 112, -100 }, (await Ledger()).Ledger.Select(entry => entry.AmountCents));
    }

    [Theory]
    [InlineData(MinesRoundStatus.CashedOut, 1)]
    [InlineData(MinesRoundStatus.Cleared, 22)]
    public async Task LegacyJsonTerminalRoundAndReceipt_KeepOriginalPayoutAndNeverCreditAgain(MinesRoundStatus status, int safeReveals)
    {
        var payout = MinesRules.ReturnCents(100, 3, safeReveals, MinesRules.LegacyHouseEdgeBasisPoints, MinesRules.LegacyPayoutPolicyVersion);
        var state = new MinesRoundState(100, 3, [0, 1, 2], [.. Enumerable.Range(3, safeReveals)], status, payout);
        var now = DateTimeOffset.UtcNow;
        var id = MinesService.Hash($"{Player}\n{Key(1)}");
        var receipt = LegacySession(99_900 + payout, id, state);
        var commandId = MinesService.Hash($"{Player}\n{Key(3)}");
        var fingerprint = status == MinesRoundStatus.CashedOut ? $"cashout:{id}" : $"reveal:{id}:24";
        var ledger = new FileMinesPracticeStore.PracticeLedger
        {
            Balances = { [Player] = receipt.BalanceCents }, Latest = { [Player] = id },
            Rounds = { [id] = new(id, Player, state, 3, now, now) },
            Commands = { [commandId] = new(Player, fingerprint, receipt) },
            Ledger = { new(commandId, Player, id, payout, receipt.BalanceCents, "mines-payout", now) },
        };
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(LedgerPath, WithoutHouseEdge(ledger));

        async Task<MinesSessionResponse> Replay() => status == MinesRoundStatus.CashedOut
            ? await Service().CashOutAsync(Player, id, new(Key(3)))
            : await Service().RevealAsync(Player, id, new(Key(3), 24));
        var resumed = await Service().SessionAsync(Player);
        Assert.Equal(payout, resumed.Round!.PayoutCents);
        Assert.Equal(MinesRules.LegacyHouseEdgeBasisPoints, resumed.Round.HouseEdgeBasisPoints);
        Assert.Equal(MinesRules.HouseEdgeBasisPoints, resumed.HouseEdgeBasisPoints);
        Equal(receipt, await Replay());
        Equal(resumed, await Service().SessionAsync(Player));
        var next = await Service().StartAsync(Player, new(Key(4), 100, 3));
        Equal(receipt, await Replay());
        Equal(next, await Service().SessionAsync(Player));
        Assert.Equal(2, (await Ledger()).Ledger.Count);
    }

    [Theory]
    [InlineData(MinesRules.LegacyHouseEdgeBasisPoints, 11_250)]
    [InlineData(MinesRules.HouseEdgeBasisPoints, 10_227)]
    public async Task PreviouslyAcceptedWagerOutsideNewLimits_ReplaysResumesAndSettlesWithoutRepricing(int edge, long expectedPayout)
    {
        const long acceptedWager = 10_000;
        var state = new MinesRoundState(acceptedWager, 3, [0, 1, 2], [], MinesRoundStatus.Active, 0, edge, MinesRules.LegacyPayoutPolicyVersion);
        var now = DateTimeOffset.UtcNow;
        var id = MinesService.Hash($"{Player}\n{Key(1)}");
        var receipt = LegacySession(90_000, id, state);
        var ledger = new FileMinesPracticeStore.PracticeLedger
        {
            Balances = { [Player] = 90_000 }, Latest = { [Player] = id },
            Rounds = { [id] = new(id, Player, state, 1, now, now) },
            Commands = { [MinesService.Hash($"{Player}\n{Key(1)}")] = new(Player, "start:10000:3", receipt) },
        };
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(LedgerPath, edge == MinesRules.LegacyHouseEdgeBasisPoints ? WithoutHouseEdge(ledger) : WithoutPayoutPolicy(ledger));
        Equal(receipt, await Service().StartAsync(Player, new(Key(1), acceptedWager, 3)));
        var resumed = await Service().SessionAsync(Player);
        Assert.Equal(100, resumed.MinimumWagerCents);
        Assert.Equal(500, resumed.MaximumWagerCents);
        Assert.Equal(acceptedWager, resumed.Round!.WagerCents);
        await Service().RevealAsync(Player, id, new(Key(2), 24));
        var paid = await Service().CashOutAsync(Player, id, new(Key(3)));
        Assert.Equal(expectedPayout, paid.Round!.PayoutCents);
        Assert.Equal(90_000 + expectedPayout, paid.BalanceCents);
        Equal(receipt, await Service().StartAsync(Player, new(Key(1), acceptedWager, 3)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service().StartAsync(Player, new(Key(4), acceptedWager, 3)));
        Assert.Equal(90_000 + expectedPayout, (await Service().SessionAsync(Player)).BalanceCents);
    }

    [Fact]
    public async Task InsufficientFundsAndInvalidActions_NeverDebitOrGenerateBoard()
    {
        var generator = new FixedBoard(3);
        var service = new MinesService(new FileMinesPracticeStore(LedgerPath, 100), new MinesEngine(generator));
        await Assert.ThrowsAsync<MinesInsufficientFundsException>(() => service.StartAsync(Player, new(Key(1), 101, 3)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync(Player, new(Key(1), 99, 3)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync(Player, new(Key(1), 501, 3)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync(Player, new(Key(1), 100, 25)));
        Assert.Equal(0, generator.Calls);
        Assert.False(File.Exists(LedgerPath));
        var id = (await service.StartAsync(Player, new(Key(1), 100, 3))).Round!.RoundId;
        await Assert.ThrowsAsync<MinesConflictException>(() => service.CashOutAsync(Player, id, new(Key(2))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RevealAsync(Player, id, new(Key(2), 25)));
        await service.RevealAsync(Player, id, new(Key(2), 24));
        await Assert.ThrowsAsync<MinesConflictException>(() => service.RevealAsync(Player, id, new(Key(3), 24)));
        Assert.Single((await Ledger()).Ledger);
    }

    private async Task<FileMinesPracticeStore.PracticeLedger> Ledger() => JsonSerializer.Deserialize<FileMinesPracticeStore.PracticeLedger>(await File.ReadAllTextAsync(LedgerPath))!;
    internal static MinesSessionResponse LegacySession(long balance, string id, MinesRoundState state, bool practice = true)
    {
        var view = new MinesEngine().Project(state);
        return new(balance, practice, new(id, view.WagerCents, view.MineCount, view.Status switch
        {
            MinesRoundStatus.Active => "active", MinesRoundStatus.CashedOut => "cashed-out", MinesRoundStatus.Cleared => "cleared",
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        }, view.SafeReveals, view.GemsRemaining, view.CurrentMultiplier, view.NextMultiplier, view.CashOutCents,
            view.CanCashOut, view.PayoutCents, view.Tiles.Select(tile => new MinesTileResponse(tile.Index, tile.IsRevealed,
                tile.Kind.ToString().ToLowerInvariant())).ToArray(), view.HouseEdgeBasisPoints, view.PayoutPolicyVersion, view.NextMinePayoutCents),
            HouseEdgeBasisPoints: state.HouseEdgeBasisPoints, PayoutPolicyVersion: state.PayoutPolicyVersion);
    }
    internal static string WithoutHouseEdge(object value)
    {
        var node = JsonSerializer.SerializeToNode(value)!;
        static void Remove(JsonNode? item)
        {
            if (item is JsonObject record)
            {
                record.Remove(nameof(MinesSessionResponse.HouseEdgeBasisPoints));
                record.Remove(nameof(MinesSessionResponse.PayoutPolicyVersion));
                record.Remove(nameof(MinesRoundResponse.NextMinePayoutCents));
                foreach (var child in record) Remove(child.Value);
            }
            else if (item is JsonArray array) foreach (var child in array) Remove(child);
        }
        Remove(node);
        return node.ToJsonString();
    }
    internal static string WithoutPayoutPolicy(object value)
    {
        var node = JsonSerializer.SerializeToNode(value)!;
        static void Remove(JsonNode? item)
        {
            if (item is JsonObject record)
            {
                record.Remove(nameof(MinesSessionResponse.PayoutPolicyVersion));
                record.Remove(nameof(MinesRoundResponse.NextMinePayoutCents));
                foreach (var child in record) Remove(child.Value);
            }
            else if (item is JsonArray array) foreach (var child in array) Remove(child);
        }
        Remove(node);
        return node.ToJsonString();
    }
    private static void Equal(MinesSessionResponse expected, MinesSessionResponse actual) => Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    internal sealed class FixedBoard(int count) : IMinesBoardGenerator
    {
        public int Calls { get; private set; }
        public ImmutableArray<int> Generate(int mineCount) { Calls++; return [.. Enumerable.Range(0, mineCount == count ? count : mineCount)]; }
    }
}
