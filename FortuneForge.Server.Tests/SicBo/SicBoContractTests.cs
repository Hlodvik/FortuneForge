using FortuneForge.Games.SicBo;
using FortuneForge.Server.Dice.SicBo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.SicBo;

public sealed class SicBoContractTests
{
    [Fact]
    public void FeatureGate_IsDisabledByDefaultAndRequiresExplicitTrueConfiguration()
    {
        var disabled = new ConfigurationBuilder().Build();
        var enabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Features:SicBoEnabled"] = "true" }).Build();

        Assert.False(SicBoController.IsEnabled(disabled));
        Assert.True(SicBoController.IsEnabled(enabled));
    }

    [Fact]
    public async Task DisabledFeature_ReturnsServiceUnavailableBeforeAccountOrStoreAccess()
    {
        var controller = new SicBoController(null!, null!, new ConfigurationBuilder().Build(), NullLogger<SicBoController>.Instance);
        var responses = new ActionResult[]
        {
            await controller.Status(CancellationToken.None),
            await controller.Start(new CreateSicBoRoundRequest([Bet("small", 1m)]), "sic-bo-start-0001", CancellationToken.None),
            await controller.Get(new string('a', 64), CancellationToken.None),
        };

        Assert.All(responses, response =>
        {
            var unavailable = Assert.IsType<ObjectResult>(response);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("sic-bo-disabled", Assert.IsType<SicBoErrorResponse>(unavailable.Value).Code);
        });
    }

    [Fact]
    public async Task Service_SettlesAMultiBetSlipAndRecordsItOnlyOnce()
    {
        var store = new InMemorySicBoStore();
        var service = new SicBoService(store, TimeProvider.System, () => new SicBoRoll(1, 2, 3));
        var request = new CreateSicBoRoundRequest(
        [
            Bet("small", 10m), Bet("single-number", 3m, face: 2),
            Bet("specific-triple", 1m, face: 1), Bet("total", 2m, total: 6),
        ]);

        var first = await service.StartAsync("player-1", request, "sic-bo-start-0002", CancellationToken.None);
        var replay = await service.StartAsync("player-1", request, "sic-bo-start-0002", CancellationToken.None);

        Assert.Equal("settled", first.Phase);
        Assert.Equal([1, 2, 3], first.Dice);
        Assert.Equal(16m, first.TotalStaked);
        Assert.Equal(66m, first.TotalReturn);
        Assert.Equal(50m, first.Profit);
        Assert.Equal(1_050m, first.Balance);
        Assert.Equal([true, true, false, true], first.Settlements.Select(settlement => settlement.Won));
        Assert.Equal(first.RoundId, replay.RoundId);
        Assert.Equal(1, store.EventCount);
        Assert.Equal(2, store.LedgerEntryCount);
        Assert.Equal(105_000, store.BalanceCents);
    }

    [Fact]
    public async Task Service_RejectsUnpricedBetsAndChangedIdempotentSlip()
    {
        var store = new InMemorySicBoStore();
        var service = new SicBoService(store, TimeProvider.System, () => new SicBoRoll(1, 2, 3));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync("player-1", new CreateSicBoRoundRequest([Bet("small", 1.5m)]), "sic-bo-start-0003", CancellationToken.None));
        await service.StartAsync("player-1", new CreateSicBoRoundRequest([Bet("small", 1m)]), "sic-bo-start-0004", CancellationToken.None);
        await Assert.ThrowsAsync<SicBoRoundConflictException>(() => service.StartAsync("player-1", new CreateSicBoRoundRequest([Bet("big", 1m)]), "sic-bo-start-0004", CancellationToken.None));
    }

    [Fact]
    public void BetLimit_CapsEachSpotAtTenRandIncludingRepeatedChips()
    {
        Assert.Equal(1_000, SicBoMoney.MaximumStakeCents);
        Assert.Equal(1_000, SicBoMoney.ParseBet(Bet("small", 10m)).StakeCents);
        Assert.Throws<ArgumentOutOfRangeException>(() => SicBoMoney.ParseBet(Bet("small", 11m)));

        Assert.Equal(2, SicBoMoney.ParseBets([Bet("small", 5m), Bet("small", 5m)]).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SicBoMoney.ParseBets([Bet("small", 5m), Bet("small", 6m)]));
        Assert.Equal(2, SicBoMoney.ParseBets([Bet("small", 10m), Bet("big", 10m)]).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SicBoMoney.ParseBets([Bet("two-number-combination", 5m, firstFace: 1, secondFace: 2),
                Bet("two-number-combination", 6m, firstFace: 2, secondFace: 1)]));
    }

    [Fact]
    public void PreviouslyStoredBetsRemainReadableAndAuditable()
    {
        var legacyBet = new SicBoStoredBet("small", 10_000, null, null, null, null);
        Assert.Equal(100m, SicBoMoney.ToDomain(legacyBet).Stake);
        Assert.True(SicBoOutcomeAudit.Verify(
            new SicBoStoreRound("legacy", "alice", [legacyBet], new SicBoRoll(1, 2, 3)),
            10_000, 20_000));
    }

    [Fact]
    public void DatabaseOutcomeCheckerRecalculatesEveryBetOnTheSlip()
    {
        var slip = SicBoMoney.ParseBets([
            Bet("small", 10m), Bet("single-number", 3m, face: 2),
            Bet("specific-triple", 1m, face: 1), Bet("total", 2m, total: 6)
        ]);
        var round = new SicBoStoreRound("round", "alice", slip, new SicBoRoll(1, 2, 3));
        Assert.True(SicBoOutcomeAudit.Verify(round, 1_600, 6_600));
        Assert.False(SicBoOutcomeAudit.Verify(round, 1_600, 6_601));
        Assert.False(SicBoOutcomeAudit.Verify(round with { Roll = new SicBoRoll(6, 6, 6) }, 1_600, 6_600));
    }

    private static SicBoBetRequest Bet(string kind, decimal stake, int? face = null, int? total = null, int? firstFace = null, int? secondFace = null) => new(kind, stake, face, total, firstFace, secondFace);

    private sealed class InMemorySicBoStore : ISicBoStore
    {
        private SicBoStoreRound? round;
        private string? idempotencyKey;

        public long BalanceCents { get; private set; } = 100_000;
        public int LedgerEntryCount { get; private set; }
        public int EventCount { get; private set; }

        public Task<SicBoStoreResult> StartAsync(string userId, string key, IReadOnlyList<SicBoStoredBet> bets, SicBoRoll roll, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            if (round is not null)
            {
                if (round.UserId != userId || idempotencyKey != key || !round.Bets.SequenceEqual(bets)) throw new SicBoRoundConflictException("slip conflict");
                return Task.FromResult(new SicBoStoreResult(round, BalanceCents));
            }
            var totalStake = checked(bets.Sum(bet => bet.StakeCents));
            if (BalanceCents < totalStake) throw new SicBoInsufficientCreditsException(BalanceCents, totalStake);
            var totalReturn = checked(bets.Sum(bet => ToCents(SicBoPaytable.Settle(SicBoMoney.ToDomain(bet), roll).TotalReturn)));
            BalanceCents = checked(BalanceCents - totalStake + totalReturn);
            round = new SicBoStoreRound(new string('d', 64), userId, bets.ToArray(), roll);
            idempotencyKey = key;
            EventCount++;
            LedgerEntryCount += totalReturn > 0 ? 2 : 1;
            return Task.FromResult(new SicBoStoreResult(round, BalanceCents));
        }

        public Task<SicBoStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken) =>
            Task.FromResult<SicBoStoreResult?>(round is not null && round.UserId == userId && round.RoundId == roundId ? new SicBoStoreResult(round, BalanceCents) : null);

        private static long ToCents(decimal value) => checked((long)(value * 100m));
    }
}
