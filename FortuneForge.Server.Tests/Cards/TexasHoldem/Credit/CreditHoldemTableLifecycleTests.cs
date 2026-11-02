using FortuneForge.Server.Bots;
using FortuneForge.Server.Cards.TexasHoldem.Credit;
using Xunit;

namespace FortuneForge.Server.Tests.Cards.TexasHoldem.Credit;

public sealed class CreditHoldemTableLifecycleTests
{
    private static readonly DateTime Start = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CompletedHandStillAllowsNextHandAtAnOpenTable()
    {
        var (store, _, result) = await CompletedTable();
        var next = Assert.IsType<CreditHoldemMatchSessionResponse>((await store.NextHandAsync(
            "owner", result.MatchId, result.Version, "next", 22, Start.AddSeconds(9), default)).Session);
        Assert.Equal(result.MatchId, next.Table.MatchId);
        Assert.Equal(2, next.Table.HandNumber);
    }

    [Fact]
    public async Task NextHandCannotBypassExpiredSeatBeforeTheWorkerOrSessionPollRuns()
    {
        var (store, _, result) = await CompletedTable();
        var expires = Start.AddSeconds(8).Add(CreditHoldemTableCoordinator.ResultInactivityDuration);
        await Assert.ThrowsAsync<CreditHoldemConflictException>(() => store.NextHandAsync(
            "owner", result.MatchId, result.Version, "late-next", 22, expires, default));
        Assert.IsType<CreditHoldemIdleSessionResponse>((await store.GetSessionAsync("owner", expires, default)).Session);
        Assert.Single((await store.HistoryAsync("owner", 50, default)).Items);
    }

    [Fact]
    public async Task ClosedTableStaysClosedWhenPopulationCapacityReturnsAndHistoryRemainsReadable()
    {
        var (store, population, result) = await CompletedTable();
        population.KeepTableOpen = false;
        Assert.IsType<CreditHoldemIdleSessionResponse>((await store.NextHandAsync(
            "owner", result.MatchId, result.Version, "close", 22, Start.AddSeconds(9), default)).Session);
        var closed = store.MatchForTest(result.MatchId);
        var closedVersion = closed.Version;
        var wallet = store.Balance("owner");
        var ledger = store.LedgerCount;

        // Population capacity can change after closure; it must not revive the old table ID.
        population.KeepTableOpen = true;
        await store.SweepAsync(Start.AddSeconds(12), default);
        Assert.Equal("completed", store.MatchForTest(result.MatchId).Status);
        Assert.Equal(closedVersion, store.MatchForTest(result.MatchId).Version);
        Assert.Equal(1, store.MatchForTest(result.MatchId).HandNumber);
        foreach (var user in new[] { "owner", "stranger" })
        {
            await Assert.ThrowsAsync<CreditHoldemConflictException>(() => store.NextHandAsync(
                user, result.MatchId, closedVersion, $"old-next-{user}", 23, Start.AddSeconds(13), default));
            await Assert.ThrowsAsync<CreditHoldemConflictException>(() => store.ActionAsync(
                user, result.MatchId, new(CreditHoldemActions.Check, closedVersion), $"old-action-{user}", Start.AddSeconds(13), default));
        }
        Assert.Equal(wallet, store.Balance("owner"));
        Assert.Equal(ledger, store.LedgerCount);
        var item = Assert.Single((await store.HistoryAsync("owner", 50, default)).Items);
        Assert.Equal(result.MatchId, item.MatchId);
        Assert.Empty((await store.HistoryAsync("stranger", 50, default)).Items);

        _ = await store.JoinAsync("owner", "Owner", 0, "fresh-join", 24, Start.AddSeconds(14), default, buyInCents: 1_000);
        var fresh = Assert.IsType<CreditHoldemMatchSessionResponse>((await store.GetSessionAsync(
            "owner", Start.AddSeconds(14).Add(CreditHoldemEngine.HumanGrace), default)).Session);
        Assert.NotEqual(result.MatchId, fresh.Table.MatchId);
    }

    private static async Task<(InMemoryCreditHoldemStore, MutablePopulation, CreditHoldemResultSessionResponse)> CompletedTable()
    {
        var population = new MutablePopulation();
        var store = new InMemoryCreditHoldemStore(allowSingleHumanBotFill: true, managedPopulationDirector: population);
        store.SetBalance("owner", 10_000);
        _ = await store.JoinAsync("owner", "Owner", 0, "join", 17, Start, default, buyInCents: 2_500);
        var session = Assert.IsType<CreditHoldemMatchSessionResponse>((await store.GetSessionAsync(
            "owner", Start.Add(CreditHoldemEngine.HumanGrace), default)).Session);
        CreditHoldemEngine.ForceComplete(store.MatchForTest(session.Table.MatchId), Start.AddSeconds(8));
        var result = Assert.IsType<CreditHoldemResultSessionResponse>((await store.GetSessionAsync(
            "owner", Start.AddSeconds(8), default)).Session);
        return (store, population, result);
    }

    private sealed class MutablePopulation : IManagedTablePopulationDirector
    {
        public bool KeepTableOpen { get; set; } = true;
        public ManagedTablePopulationDecision Observe(ManagedTablePopulationObservation observation) =>
            new(KeepTableOpen,
                observation.ManagedPlayers == 0 ? Math.Max(0, observation.MinimumOccupancy - observation.HumanPlayers) : 0,
                observation.HumanPlayers == 0);
        public void Close(string gameId, string tableId) { }
    }
}
