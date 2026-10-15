using FortuneForge.Games.HouseHoldem;
using FortuneForge.Server.Cards.HouseHoldem;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.HouseHoldem;

public sealed class HouseHoldemContractTests
{
    [Fact]
    public async Task ContendedTransactionStartsFreshAttemptAfterFirestoreAborts()
    {
        var attempts = 0;
        var result = await HouseHoldemFirestoreStore.RunWithContentionRetryAsync(() =>
        {
            attempts++;
            if (attempts < 3)
                throw new RpcException(new Status(StatusCode.Aborted, "Transaction lock timeout."));
            return Task.FromResult(42);
        }, default);

        Assert.Equal(42, result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public void FeatureGate_RequiresExplicitEnablement()
    {
        Assert.False(HouseHoldemController.IsEnabled(new ConfigurationBuilder().Build()));
        Assert.True(HouseHoldemController.IsEnabled(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Features:HouseHoldemEnabled"] = "true" })
            .Build()));
    }

    [Fact]
    public async Task DisabledFeature_StopsBeforeAccountAndStoreAccess()
    {
        var controller = new HouseHoldemController(null!, null!, new ConfigurationBuilder().Build(),
            NullLogger<HouseHoldemController>.Instance);
        var id = new string('a', 64);
        var responses = new ActionResult[]
        {
            await controller.Status(default),
            await controller.Start(new CreateHouseHoldemRoundRequest(1), "house_holdem_start_0001", default),
            await controller.Get(id, default),
            await controller.Action(id, new HouseHoldemActionRequest("check"), "house_holdem_action_0001", default),
            await controller.Advance(id, "house_holdem_advance_0001", default),
        };
        Assert.All(responses, response =>
        {
            var unavailable = Assert.IsType<ObjectResult>(response);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("house-holdem-disabled", Assert.IsType<HouseHoldemErrorResponse>(unavailable.Value).Code);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.5)]
    [InlineData(1.01)]
    [InlineData(101)]
    public void AnteRejectsOutOfRangeOrFractionalRand(decimal amount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HouseHoldemMoney.ToAnteCents(amount));

    [Fact]
    public void AnteAndTripsHaveIndependentMaximums()
    {
        Assert.Equal(10_000, HouseHoldemMoney.ToAnteCents(100));
        Assert.Equal(50_000, HouseHoldemMoney.ToTripsCents(500));
        Assert.Throws<ArgumentOutOfRangeException>(() => HouseHoldemMoney.ToTripsCents(501));
    }

    [Fact]
    public void ResponseRedactsDealerAndUndealtBoardUntilSettlement()
    {
        var deck = HouseHoldemRules.CreateShuffledDeck();
        var round = HouseHoldemRules.StartRound(deck, 100, 100);
        var id = new string('a', 64);
        var preflop = HouseHoldemService.ToResponse(new HouseHoldemStoreResult(id, round, 9_700));
        Assert.Equal("preflop", preflop.Phase);
        Assert.Equal(2, preflop.PlayerCards.Count);
        Assert.Null(preflop.DealerCards);
        Assert.Empty(preflop.Board);
        Assert.Null(preflop.Result);
        Assert.Equal(["check", "play-3x", "play-4x"], preflop.AllowedActions);
        Assert.Equal(3m, preflop.TotalWager);

        HouseHoldemRules.ApplyAction(round, "play-4x");
        var flop = HouseHoldemService.ToResponse(new HouseHoldemStoreResult(id, round, 9_300));
        Assert.Equal("flop", flop.Phase);
        Assert.Equal(3, flop.Board.Count);
        Assert.Null(flop.DealerCards);
        Assert.Empty(flop.AllowedActions);
        Assert.True(flop.CanAdvance);

        HouseHoldemRules.Advance(round);
        var river = HouseHoldemService.ToResponse(new HouseHoldemStoreResult(id, round, 9_300));
        Assert.Equal("river", river.Phase);
        Assert.Equal(5, river.Board.Count);
        Assert.Null(river.DealerCards);
        Assert.True(river.CanAdvance);

        HouseHoldemRules.Advance(round);
        var settled = HouseHoldemService.ToResponse(new HouseHoldemStoreResult(id, round, 9_300 + round.PayoutCents));
        Assert.Equal("settled", settled.Phase);
        Assert.Equal(2, settled.DealerCards!.Count);
        Assert.NotNull(settled.Result);
        Assert.NotNull(settled.PayoutBreakdown);
        Assert.Equal(7m, settled.TotalWager);
        Assert.Equal(round.PayoutCents / 100m, settled.Payout);
    }
}
