using FortuneForge.Server.Cards.Blackjack;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Blackjack;

public sealed class BlackjackContractTests
{
    [Fact]
    public void Controllers_ExposeRealAndDemoStatusGameAndActionRoutes()
    {
        AssertControllerRoutes(typeof(BlackjackController), "api/cards/blackjack");
        AssertControllerRoutes(typeof(DemoBlackjackController), "api/cards/blackjack/demo");
    }

    [Fact]
    public void Status_DeclaresRulesAndSafeWagerLimits()
    {
        var status = BlackjackHttp.Status();

        Assert.True(status.Available);
        Assert.Equal(0.50m, status.MinimumWager);
        Assert.Equal(100m, status.MaximumWager);
        Assert.Equal(0.50m, status.WagerIncrement);
        Assert.Equal("3:2", status.BlackjackPayout);
        Assert.Equal("Dealer stands on all 17s", status.DealerRule);
        Assert.True(status.DoubleAllowed);
        Assert.False(status.SplitAllowed);
        Assert.False(status.InsuranceAllowed);
    }

    [Fact]
    public void FeatureGate_IsDisabledByDefaultAndRequiresExplicitTrueConfiguration()
    {
        var disabled = new ConfigurationBuilder().Build();
        var enabled = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Features:BlackjackEnabled"] = "true"
            })
            .Build();

        Assert.False(BlackjackController.IsEnabled(disabled));
        Assert.True(BlackjackController.IsEnabled(enabled));
    }

    [Fact]
    public async Task DisabledFeature_ReturnsServiceUnavailableBeforeAccountOrStoreAccess()
    {
        var controller = new BlackjackController(
            null!,
            null!,
            new ConfigurationBuilder().Build(),
            NullLogger<BlackjackController>.Instance);

        var responses = new ActionResult[]
        {
            await controller.Status(CancellationToken.None),
            await controller.Start(
                new BlackjackStartRequest(5m),
                "start_request_0004",
                CancellationToken.None),
            await controller.Get(new string('a', 64), CancellationToken.None),
            await controller.Act(
                new string('a', 64),
                new BlackjackActionRequest(BlackjackActions.Stand, 1),
                "stand_request_0004",
                CancellationToken.None)
        };

        Assert.All(responses, response =>
        {
            var unavailable = Assert.IsType<ObjectResult>(response);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal(
                "blackjack-disabled",
                unavailable.Value!.GetType().GetProperty("code")!.GetValue(unavailable.Value));
        });
    }

    [Fact]
    public async Task Service_HidesDealerHoleCardUntilSettlement()
    {
        var store = new InMemoryBlackjackStore();
        var service = new BlackjackService(store);

        var started = await service.StartAsync(
            "player",
            new BlackjackStartRequest(5m),
            "start_request_0001",
            CancellationToken.None);

        Assert.Equal(BlackjackStatuses.Active, started.Status);
        Assert.True(started.Dealer.Cards[1].Hidden);
        Assert.Null(started.Dealer.Score);

        var completed = await service.ActAsync(
            "player",
            started.GameId,
            new BlackjackActionRequest(BlackjackActions.Stand, started.Version),
            "stand_request_0001",
            CancellationToken.None);

        Assert.Equal(BlackjackStatuses.Completed, completed.Status);
        Assert.All(completed.Dealer.Cards, card => Assert.False(card.Hidden));
        Assert.NotNull(completed.Dealer.Score);
    }

    [Fact]
    public async Task Service_RepeatedStartAndDoubleKeys_DoNotChargeTwice()
    {
        var store = new InMemoryBlackjackStore();
        var service = new BlackjackService(store);

        var first = await service.StartAsync(
            "player",
            new BlackjackStartRequest(5m),
            "start_request_0002",
            CancellationToken.None);
        var repeatedStart = await service.StartAsync(
            "player",
            new BlackjackStartRequest(5m),
            "start_request_0002",
            CancellationToken.None);
        Assert.Equal(first.GameId, repeatedStart.GameId);
        Assert.Equal(9_500, store.BalanceCents);

        var doubled = await service.ActAsync(
            "player",
            first.GameId,
            new BlackjackActionRequest("  DOUBLE  ", first.Version),
            "double_request_0002",
            CancellationToken.None);
        var balanceAfterDouble = store.BalanceCents;
        var repeatedDouble = await service.ActAsync(
            "player",
            first.GameId,
            new BlackjackActionRequest("double", first.Version),
            "double_request_0002",
            CancellationToken.None);

        Assert.Equal(doubled.Version, repeatedDouble.Version);
        Assert.Equal(balanceAfterDouble, store.BalanceCents);
        Assert.Equal(2, store.LedgerEntries);
    }

    [Fact]
    public async Task Service_RejectsStaleVersionBeforeApplyingAnotherAction()
    {
        var store = new InMemoryBlackjackStore();
        var service = new BlackjackService(store);
        var started = await service.StartAsync(
            "player",
            new BlackjackStartRequest(5m),
            "start_request_0003",
            CancellationToken.None);
        _ = await service.ActAsync(
            "player",
            started.GameId,
            new BlackjackActionRequest(BlackjackActions.Hit, started.Version),
            "first_hit_request_0003",
            CancellationToken.None);

        await Assert.ThrowsAsync<BlackjackConflictException>(() => service.ActAsync(
            "player",
            started.GameId,
            new BlackjackActionRequest(BlackjackActions.Stand, started.Version),
            "stale_stand_request_3",
            CancellationToken.None));
    }

    private static void AssertControllerRoutes(Type controllerType, string prefix)
    {
        var route = Assert.Single(controllerType.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>());
        Assert.Equal(prefix, route.Template);
        var methods = controllerType.GetMethods()
            .Where(method => method.DeclaringType == controllerType)
            .Select(method => new
            {
                method.Name,
                Http = method.GetCustomAttributes(false).OfType<HttpMethodAttribute>().SingleOrDefault(),
                Rate = method.GetCustomAttributes(false).OfType<EnableRateLimitingAttribute>().SingleOrDefault()
            })
            .Where(item => item.Http is not null)
            .ToArray();

        Assert.Equal(4, methods.Length);
        Assert.Contains(methods, item => item.Name == "Status" && item.Http!.Template == "status" && item.Rate is not null);
        Assert.Contains(methods, item => item.Name == "Start" && item.Http!.Template == "games" && item.Http.HttpMethods.Contains("POST"));
        Assert.Contains(methods, item => item.Name == "Get" && item.Http!.Template == "games/{gameId}" && item.Http.HttpMethods.Contains("GET"));
        Assert.Contains(methods, item => item.Name == "Act" && item.Http!.Template == "games/{gameId}/actions" && item.Http.HttpMethods.Contains("POST"));
    }

    private sealed class InMemoryBlackjackStore : IBlackjackStore
    {
        private BlackjackGame? game;
        private readonly Dictionary<string, string> actions = new(StringComparer.Ordinal);

        public long BalanceCents { get; private set; } = 10_000;
        public int LedgerEntries { get; private set; }

        public Task<BlackjackStoreResult> StartAsync(
            string userId,
            string idempotencyKey,
            long wagerCents,
            IReadOnlyList<string> shuffledDeck,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            var gameId = FirestoreBlackjackStore.CreateLookupKey($"{userId}\n{idempotencyKey}");
            if (game is not null)
            {
                return Task.FromResult(new BlackjackStoreResult(game, BalanceCents));
            }
            BalanceCents -= wagerCents;
            LedgerEntries++;
            game = BlackjackRules.Deal(
                gameId,
                userId,
                wagerCents,
                BlackjackRulesTests.Deck(
                    "5|spades", "9|clubs", "6|hearts", "7|diamonds",
                    "2|clubs", "10|clubs"),
                nowUtc);
            return Task.FromResult(new BlackjackStoreResult(game, BalanceCents));
        }

        public Task<BlackjackStoreResult?> GetAsync(
            string userId,
            string gameId,
            CancellationToken cancellationToken) =>
            Task.FromResult<BlackjackStoreResult?>(
                game is not null && game.GameId == gameId
                    ? new BlackjackStoreResult(game, BalanceCents)
                    : null);

        public Task<BlackjackStoreResult> ActAsync(
            string userId,
            string gameId,
            string idempotencyKey,
            int expectedVersion,
            string action,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            Assert.NotNull(game);
            if (actions.TryGetValue(idempotencyKey, out var storedAction))
            {
                Assert.Equal(action, storedAction, ignoreCase: true);
                return Task.FromResult(new BlackjackStoreResult(game!, BalanceCents));
            }
            if (game!.Version != expectedVersion)
            {
                throw new BlackjackConflictException("stale version");
            }
            if (string.Equals(action, BlackjackActions.Double, StringComparison.OrdinalIgnoreCase))
            {
                BalanceCents -= game.WagerCents;
                LedgerEntries++;
            }
            game = BlackjackRules.ApplyAction(game, action, nowUtc);
            if (game.Status == BlackjackStatuses.Completed && game.PayoutCents > 0)
            {
                BalanceCents += game.PayoutCents;
            }
            actions.Add(idempotencyKey, action);
            return Task.FromResult(new BlackjackStoreResult(game, BalanceCents));
        }
    }
}
