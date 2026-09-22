using FortuneForge.Games.Keno;
using FortuneForge.Server.Numbers.Keno;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Keno;

public sealed class KenoContractTests
{
    [Fact]
    public void FeatureGate_IsDisabledByDefaultAndRequiresExplicitTrueConfiguration()
    {
        var disabled = new ConfigurationBuilder().Build();
        var enabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Features:KenoEnabled"] = "true" }).Build();

        Assert.False(KenoController.IsEnabled(disabled));
        Assert.True(KenoController.IsEnabled(enabled));
    }

    [Fact]
    public async Task DisabledFeature_ReturnsServiceUnavailableBeforeAccountOrStoreAccess()
    {
        var controller = new KenoController(null!, null!, new ConfigurationBuilder().Build(), NullLogger<KenoController>.Instance);
        var responses = new ActionResult[]
        {
            await controller.Status(CancellationToken.None),
            await controller.Start(new CreateKenoRoundRequest(new KenoTicketRequest([3, 7, 15])), "keno-start-00001", CancellationToken.None),
            await controller.Get(new string('a', 64), CancellationToken.None),
        };

        Assert.All(responses, response =>
        {
            var unavailable = Assert.IsType<ObjectResult>(response);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("keno-disabled", Assert.IsType<KenoErrorResponse>(unavailable.Value).Code);
        });
    }

    [Fact]
    public async Task Service_RecordsAnAuthenticatedFreePlayDrawExactlyOnce()
    {
        var store = new InMemoryKenoStore();
        var service = new KenoService(store, TimeProvider.System, () => new KenoDraw([3, 7, .. Enumerable.Range(21, 18)]));
        var request = new CreateKenoRoundRequest(new KenoTicketRequest([15, 3, 7]));

        var first = await service.StartAsync("player-1", request, "keno-start-00002", CancellationToken.None);
        var replay = await service.StartAsync("player-1", request, "keno-start-00002", CancellationToken.None);

        Assert.Equal("completed", first.Phase);
        Assert.Equal([3, 7, 15], first.Ticket.Numbers);
        Assert.Equal(2, first.HitCount);
        Assert.Equal("2 hits", first.Outcome);
        Assert.Equal(1_000m, first.Balance);
        Assert.Equal(first.RoundId, replay.RoundId);
        Assert.Equal(1, store.EventCount);
        Assert.Equal(100_000, store.BalanceCents);
    }

    [Fact]
    public async Task Service_RejectsInvalidOrChangedIdempotentTickets()
    {
        var store = new InMemoryKenoStore();
        var service = new KenoService(store, TimeProvider.System, () => new KenoDraw(Enumerable.Range(1, 20)));

        await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync("player-1", new CreateKenoRoundRequest(new KenoTicketRequest([])), "keno-start-00003", CancellationToken.None));
        await service.StartAsync("player-1", new CreateKenoRoundRequest(new KenoTicketRequest([1])), "keno-start-00004", CancellationToken.None);
        await Assert.ThrowsAsync<KenoRoundConflictException>(() => service.StartAsync("player-1", new CreateKenoRoundRequest(new KenoTicketRequest([2])), "keno-start-00004", CancellationToken.None));
    }

    private sealed class InMemoryKenoStore : IKenoStore
    {
        private KenoStoreRound? round;
        private string? idempotencyKey;
        public long BalanceCents { get; private set; } = 100_000;
        public int EventCount { get; private set; }

        public Task<KenoStoreResult> StartAsync(string userId, string key, KenoTicket ticket, KenoDraw draw, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            if (round is not null)
            {
                if (round.UserId != userId || idempotencyKey != key || !round.Ticket.Numbers.SequenceEqual(ticket.Numbers)) throw new KenoRoundConflictException("ticket conflict");
                return Task.FromResult(new KenoStoreResult(round, BalanceCents));
            }
            var played = KenoRoundEngine.Play(ticket, draw);
            round = new KenoStoreRound(new string('e', 64), userId, ticket, draw, played.Result.HitCount);
            idempotencyKey = key;
            EventCount++;
            return Task.FromResult(new KenoStoreResult(round, BalanceCents));
        }

        public Task<KenoStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken) =>
            Task.FromResult<KenoStoreResult?>(round is not null && round.UserId == userId && round.RoundId == roundId ? new KenoStoreResult(round, BalanceCents) : null);
    }
}
