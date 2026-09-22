using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;
using FortuneForge.Server.Cards.CasinoWar;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.CasinoWar;

public sealed class CasinoWarContractTests
{
    [Fact]
    public void FeatureGate_IsDisabledByDefaultAndRequiresExplicitTrueConfiguration()
    {
        var disabled = new ConfigurationBuilder().Build();
        var enabled = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Features:CasinoWarEnabled"] = "true" })
            .Build();

        Assert.False(CasinoWarController.IsEnabled(disabled));
        Assert.True(CasinoWarController.IsEnabled(enabled));
    }

    [Fact]
    public async Task DisabledFeature_ReturnsServiceUnavailableBeforeAccountOrStoreAccess()
    {
        var controller = new CasinoWarController(null!, null!, new ConfigurationBuilder().Build(), NullLogger<CasinoWarController>.Instance);
        var responses = new ActionResult[]
        {
            await controller.Status(CancellationToken.None),
            await controller.Start(new CreateCasinoWarRoundRequest(1m, 0m), "casino-war-start-0001", CancellationToken.None),
            await controller.Get(new string('a', 64), CancellationToken.None),
            await controller.Decide(new string('a', 64), new DecideCasinoWarRoundRequest("surrender"), "casino-war-decision-0001", CancellationToken.None),
        };

        Assert.All(responses, response =>
        {
            var unavailable = Assert.IsType<ObjectResult>(response);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("casino-war-disabled", Assert.IsType<CasinoWarErrorResponse>(unavailable.Value).Code);
        });
    }

    [Fact]
    public async Task Service_SettlesAnOpeningWinAndRecordsTheOpeningOnlyOnce()
    {
        var store = new InMemoryCasinoWarStore();
        var service = new CasinoWarService(store, TimeProvider.System, OpeningWinShoe);

        var first = await service.StartAsync("player-1", new CreateCasinoWarRoundRequest(10m, 2m), "casino-war-start-0002", CancellationToken.None);
        var replay = await service.StartAsync("player-1", new CreateCasinoWarRoundRequest(10m, 2m), "casino-war-start-0002", CancellationToken.None);

        Assert.Equal("completed", first.Phase);
        Assert.Equal("player-opening-win", first.PrimarySettlement!.Outcome);
        Assert.Equal(10m, first.PrimarySettlement.Profit);
        Assert.False(first.TieSettlement!.Won);
        Assert.Equal(-2m, first.TieSettlement.Profit);
        Assert.Equal(1_008m, first.Balance);
        Assert.Equal(first.RoundId, replay.RoundId);
        Assert.Equal(1, store.EventCount);
        Assert.Equal(2, store.LedgerEntryCount);
        Assert.Equal(100_800, store.BalanceCents);
    }

    [Fact]
    public async Task Service_ChargesWarDecisionExactlyOnceAndRejectsChangedDecision()
    {
        var store = new InMemoryCasinoWarStore();
        var service = new CasinoWarService(store, TimeProvider.System, TieThenWarWinShoe);

        var opening = await service.StartAsync("player-1", new CreateCasinoWarRoundRequest(10m, 2m), "casino-war-start-0003", CancellationToken.None);
        var first = await service.DecideAsync("player-1", opening.RoundId, new DecideCasinoWarRoundRequest("go-to-war"), "casino-war-decision-0003", CancellationToken.None);
        var replay = await service.DecideAsync("player-1", opening.RoundId, new DecideCasinoWarRoundRequest("go-to-war"), "casino-war-decision-0003", CancellationToken.None);

        Assert.Equal("completed", first.Phase);
        Assert.Equal("go-to-war", first.Decision);
        Assert.Equal("player-war-win", first.PrimarySettlement!.Outcome);
        Assert.Equal(30m, first.PrimarySettlement.TotalReturn);
        Assert.Equal(1_030m, first.Balance);
        Assert.Equal(first.RoundId, replay.RoundId);
        Assert.Equal(2, store.EventCount);
        Assert.Equal(4, store.LedgerEntryCount);
        Assert.Equal(103_000, store.BalanceCents);

        await Assert.ThrowsAsync<CasinoWarRoundConflictException>(() => service.DecideAsync(
            "player-1", opening.RoundId, new DecideCasinoWarRoundRequest("surrender"), "casino-war-decision-0004", CancellationToken.None));
    }

    [Fact]
    public async Task Service_RejectsUnpricedStakesAndChangedOpeningRequest()
    {
        var store = new InMemoryCasinoWarStore();
        var service = new CasinoWarService(store, TimeProvider.System, OpeningWinShoe);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync(
            "player-1", new CreateCasinoWarRoundRequest(1.50m, 0m), "casino-war-start-0005", CancellationToken.None));

        await service.StartAsync("player-1", new CreateCasinoWarRoundRequest(1m, 0m), "casino-war-start-0006", CancellationToken.None);
        await Assert.ThrowsAsync<CasinoWarRoundConflictException>(() => service.StartAsync(
            "player-1", new CreateCasinoWarRoundRequest(2m, 0m), "casino-war-start-0006", CancellationToken.None));
    }

    private static IReadOnlyList<PlayingCard> OpeningWinShoe() =>
    [
        new(CardRank.Ace, CardSuit.Clubs), new(CardRank.King, CardSuit.Diamonds),
        new(CardRank.Two, CardSuit.Clubs), new(CardRank.Three, CardSuit.Clubs), new(CardRank.Four, CardSuit.Clubs),
        new(CardRank.Five, CardSuit.Clubs), new(CardRank.Six, CardSuit.Clubs), new(CardRank.Seven, CardSuit.Clubs),
        new(CardRank.Eight, CardSuit.Clubs), new(CardRank.Nine, CardSuit.Clubs),
    ];

    private static IReadOnlyList<PlayingCard> TieThenWarWinShoe() =>
    [
        new(CardRank.Five, CardSuit.Clubs), new(CardRank.Five, CardSuit.Diamonds),
        new(CardRank.Two, CardSuit.Clubs), new(CardRank.Three, CardSuit.Clubs), new(CardRank.Four, CardSuit.Clubs),
        new(CardRank.Ace, CardSuit.Spades), new(CardRank.Six, CardSuit.Clubs), new(CardRank.Seven, CardSuit.Clubs),
        new(CardRank.Eight, CardSuit.Clubs), new(CardRank.King, CardSuit.Hearts),
    ];

    private sealed class InMemoryCasinoWarStore : ICasinoWarStore
    {
        private CasinoWarStoreRound? round;
        private string? startIdempotencyKey;
        private string? decisionIdempotencyKey;

        public long BalanceCents { get; private set; } = 100_000;
        public int LedgerEntryCount { get; private set; }
        public int EventCount { get; private set; }

        public Task<CasinoWarStoreResult> StartAsync(string userId, string idempotencyKey, long primaryStakeCents, long tieStakeCents, IReadOnlyList<PlayingCard> shuffledShoe, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            if (round is not null)
            {
                if (round.UserId != userId || startIdempotencyKey != idempotencyKey ||
                    CasinoWarMoney.PrimaryCents(round.Round.PrimaryStake) != primaryStakeCents ||
                    CasinoWarMoney.TieCents(round.TieSettlement?.Stake ?? 0m) != tieStakeCents)
                {
                    throw new CasinoWarRoundConflictException("start conflict");
                }

                return Task.FromResult(new CasinoWarStoreResult(round, BalanceCents));
            }

            var openingCharge = checked(primaryStakeCents + tieStakeCents);
            if (BalanceCents < openingCharge) throw new CasinoWarInsufficientCreditsException(BalanceCents, openingCharge);
            var started = CasinoWarRoundEngine.Start(shuffledShoe.Take(10).ToArray(), CasinoWarMoney.ToRand(primaryStakeCents));
            var tie = tieStakeCents == 0 ? null : CasinoWarTieBetPaytable.Settle(started.Opening, CasinoWarMoney.ToRand(tieStakeCents));
            var tieReturn = ToCents(tie?.TotalReturn ?? 0m);
            var primaryReturn = ToCents(started.Settlement?.TotalReturn ?? 0m);
            BalanceCents = checked(BalanceCents - openingCharge + tieReturn + primaryReturn);
            round = new CasinoWarStoreRound(new string('c', 64), userId, started, tie, shuffledShoe.Take(10).ToArray());
            startIdempotencyKey = idempotencyKey;
            EventCount++;
            LedgerEntryCount += 1 + (tieReturn > 0 ? 1 : 0) + (primaryReturn > 0 ? 1 : 0);
            return Task.FromResult(new CasinoWarStoreResult(round, BalanceCents));
        }

        public Task<CasinoWarStoreResult?> GetAsync(string userId, string roundId, CancellationToken cancellationToken) =>
            Task.FromResult<CasinoWarStoreResult?>(round is not null && round.UserId == userId && round.RoundId == roundId
                ? new CasinoWarStoreResult(round, BalanceCents)
                : null);

        public Task<CasinoWarStoreResult> DecideAsync(string userId, string roundId, string idempotencyKey, CasinoWarTieDecision decision, DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            if (round is null || round.UserId != userId || round.RoundId != roundId) throw new CasinoWarRoundNotFoundException();
            if (round.Round.Phase == CasinoWarRoundPhase.Completed)
            {
                if (round.Round.TieDecision != decision) throw new CasinoWarRoundConflictException("decision conflict");
                return Task.FromResult(new CasinoWarStoreResult(round, BalanceCents));
            }

            var extraCharge = decision == CasinoWarTieDecision.GoToWar ? CasinoWarMoney.PrimaryCents(round.Round.PrimaryStake) : 0;
            if (BalanceCents < extraCharge) throw new CasinoWarInsufficientCreditsException(BalanceCents, extraCharge);
            var completed = CasinoWarRoundEngine.Decide(round.Round, decision);
            var primaryReturn = ToCents(completed.Settlement!.TotalReturn);
            BalanceCents = checked(BalanceCents - extraCharge + primaryReturn);
            round = round with { Round = completed };
            decisionIdempotencyKey = idempotencyKey;
            EventCount++;
            LedgerEntryCount += (extraCharge > 0 ? 1 : 0) + (primaryReturn > 0 ? 1 : 0);
            return Task.FromResult(new CasinoWarStoreResult(round, BalanceCents));
        }

        private static long ToCents(decimal value) => checked((long)(value * 100m));
    }
}
