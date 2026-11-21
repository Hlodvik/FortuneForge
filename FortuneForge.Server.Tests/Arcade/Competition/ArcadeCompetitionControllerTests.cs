using System.Text.Json;
using FortuneForge.Server.Arcade.Competition;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

public sealed class ArcadeCompetitionControllerTests
{
    [Fact]
    public async Task CurrentAndHistoricalAtResolveNavigableJohannesburgDailyWindows()
    {
        var controller = Create(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));

        var current = Response(await controller.Get("asteroids", "daily", null, default));
        var historical = Response(await controller.Get("asteroids", "daily", "2026-09-04T12:00:00Z", default));

        Assert.Equal(new DateTimeOffset(2026, 9, 2, 22, 0, 0, TimeSpan.Zero), current.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 3, 22, 0, 0, TimeSpan.Zero), historical.StartsAtUtc);
        Assert.Equal(current.StartsAtUtc.AddDays(1), current.EndsAtUtc);
        Assert.Equal(historical.StartsAtUtc.AddDays(1), historical.EndsAtUtc);
    }

    [Fact]
    public async Task WeeklyPeriodIsAcceptedAndInvalidPeriodOrTimestampIsRejected()
    {
        var controller = Create(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));

        var weekly = Response(await controller.Get("asteroids", "weekly", null, default));
        var invalidPeriod = Assert.IsType<BadRequestObjectResult>(await controller.Get("asteroids", "monthly", null, default));
        var invalidAt = Assert.IsType<BadRequestObjectResult>(await controller.Get("asteroids", "daily", "not-a-utc-time", default));

        Assert.Equal("weekly", weekly.Period);
        Assert.Equal(weekly.StartsAtUtc.AddDays(7), weekly.EndsAtUtc);
        Assert.Equal("arcade-competition-period-invalid", Assert.IsType<ArcadeCompetitionApiError>(invalidPeriod.Value).Code);
        Assert.Equal("arcade-competition-at-invalid", Assert.IsType<ArcadeCompetitionApiError>(invalidAt.Value).Code);
    }

    [Fact]
    public async Task GameOutsideTheAllowedListIsNotFound()
    {
        var result = Assert.IsType<NotFoundObjectResult>(await Create().Get("other-arcade", "daily", null, default));

        Assert.Equal("arcade-competition-game-not-found", Assert.IsType<ArcadeCompetitionApiError>(result.Value).Code);
    }

    [Fact]
    public async Task HorseFlightIsAnAllowedLeaderboardGame()
    {
        var response = Response(await Create().Get("horse-flight", "daily", null, default));

        Assert.Equal("horse-flight", response.GameId);
        Assert.Empty(response.Leaderboard);
    }

    [Fact]
    public async Task PublicSnapshotOmitsHouseCutFields()
    {
        var response = Response(await Create().Get("asteroids", "daily", null, default));
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("houseCut", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, response.TotalUniquePlayers);
        Assert.Empty(response.Leaderboard);
    }

    [Fact]
    public async Task AllTimeIsReadOnlyAndOmitsPeriodSnapshotFields()
    {
        var controller = Create();
        var result = Assert.IsType<OkObjectResult>(await controller.Get("asteroids", "all-time", null, default));
        var response = Assert.IsType<ArcadeCompetitionAllTimeResponse>(result.Value);
        var json = JsonSerializer.Serialize(response);
        var withAt = Assert.IsType<BadRequestObjectResult>(await controller.Get("asteroids", "all-time", "2026-09-03T12:00:00Z", default));

        Assert.Equal("all-time", response.Period);
        Assert.DoesNotContain("visibleJackpot", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("entriesOpen", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("isCompleted", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refund", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("houseCut", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("arcade-competition-at-not-supported-for-all-time", Assert.IsType<ArcadeCompetitionApiError>(withAt.Value).Code);
    }

    [Fact]
    public async Task AllTimeStillUsesTheAllowedGameGuard()
    {
        var result = Assert.IsType<NotFoundObjectResult>(await Create().Get("other-arcade", "all-time", null, default));

        Assert.Equal("arcade-competition-game-not-found", Assert.IsType<ArcadeCompetitionApiError>(result.Value).Code);
    }

    [Fact]
    public void RunStatusRegistersAGetRouteAndRequiresTheWorker()
    {
        var action = typeof(ArcadeCompetitionController).GetMethod(nameof(ArcadeCompetitionController.GetRun))!;
        Assert.Equal("{gameId}/{period}/runs/{runId}", Assert.Single(action.GetCustomAttributes(typeof(HttpGetAttribute), false)
            .Cast<HttpGetAttribute>()).Template);

        var controller = Create();
        foreach (var (game, period) in new[] { ("asteroids", "free"), ("asteroids", "daily"), ("flappy", "weekly"), ("horse-flight", "daily") })
        {
            var unavailable = Assert.IsType<ObjectResult>(controller.GetRun(game, period));
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, unavailable.StatusCode);
            Assert.Equal("arcade-competition-worker-required", ErrorCode(unavailable));
        }
        Assert.IsType<NotFoundObjectResult>(controller.GetRun("other-arcade", "daily"));
        Assert.IsType<BadRequestObjectResult>(controller.GetRun("flappy", "free"));
        Assert.IsType<BadRequestObjectResult>(controller.GetRun("asteroids", "all-time"));
    }

    [Theory]
    [InlineData("asteroids")]
    [InlineData("flappy")]
    [InlineData("horse-flight")]
    public void PaidAdmissionCannotRunOnThePlatform(string game)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Features:HorseFlightEnabled"] = "true" }).Build();
        var controller = Create();
        var result = Assert.IsType<ObjectResult>(controller.StartAttempt(game, "daily", configuration));
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal("arcade-competition-worker-required", ErrorCode(result));
    }

    [Fact]
    public void PaidAndFreeMutationRoutesRetainTheirPublicContracts()
    {
        PostRoutes(nameof(ArcadeCompetitionController.StartAttempt), "{gameId}/{period}/attempts");
        PostRoutes(nameof(ArcadeCompetitionController.CompletePaidReplay), "{gameId}/{period}/runs/{runId}/replay");
        PostRoutes(nameof(ArcadeCompetitionController.FreeRun), "asteroids/free/runs", "asteroids/free/runs/{runId}/replay",
            "flappy/free/runs", "flappy/free/runs/{runId}/replay");
        PostRoutes(nameof(ArcadeCompetitionController.StartFreeHorseFlightRun), "horse-flight/free/runs");
        PostRoutes(nameof(ArcadeCompetitionController.CompleteHorseFlightReplay), "horse-flight/free/runs/{runId}/replay",
            "horse-flight/{period}/runs/{runId}/replay");
        var horseReplay = typeof(ArcadeCompetitionController).GetMethod(nameof(ArcadeCompetitionController.CompleteHorseFlightReplay))!;
        Assert.Single(horseReplay.GetCustomAttributes(typeof(RequestSizeLimitAttribute), false));
    }

    [Fact]
    public void HorseFlightMaintenanceGateAppliesToFreeAndPaidRoutes()
    {
        var controller = Create();
        var disabled = new ConfigurationBuilder().Build();
        var responses = new[]
        {
            controller.StartAttempt("horse-flight", "weekly", disabled),
            controller.StartFreeHorseFlightRun(disabled),
            controller.CompleteHorseFlightReplay(disabled),
        };
        Assert.All(responses, response =>
        {
            var result = Assert.IsType<ObjectResult>(response);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
            Assert.Equal("arcade-horse-flight-disabled", ErrorCode(result));
        });
    }

    private static void PostRoutes(string action, params string[] routes)
    {
        var method = typeof(ArcadeCompetitionController).GetMethod(action)!;
        var templates = method.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>()
            .Select(attribute => attribute.Template).Order().ToArray();
        Assert.Equal(routes.Order().ToArray(), templates);
        Assert.Equal(RateLimitPolicies.SlotSpins,
            Assert.Single(method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false).Cast<EnableRateLimitingAttribute>()).PolicyName);
    }

    private static ArcadeCompetitionController Create(DateTimeOffset? now = null) => new(
        new ArcadeCompetitionService(new EmptyStore(), new FixedTimeProvider(now ?? new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero))),
        Options.Create(new ArcadeCompetitionApiOptions()));

    private static ArcadeCompetitionSnapshotResponse Response(IActionResult result) =>
        Assert.IsType<ArcadeCompetitionSnapshotResponse>(Assert.IsType<OkObjectResult>(result).Value);

    private static string ErrorCode(ObjectResult result) =>
        Assert.IsType<ArcadeCompetitionApiError>(result.Value).Code;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class EmptyStore : IArcadeCompetitionStore
    {
        public Task<ArcadeCompetitionStartAttemptResult> StartAttemptAsync(ArcadeCompetitionAttemptStart attempt, CancellationToken cancellationToken) =>
            Task.FromResult(new ArcadeCompetitionStartAttemptResult(ArcadeCompetitionAttemptRecord.Start(attempt), false));
        public Task<ArcadeCompetitionCompleteAttemptResult> CompleteAttemptAsync(ArcadeCompetitionAttemptCompletion completion, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ArcadeCompetitionAttemptRecord>> LoadAttemptsAsync(ArcadeCompetitionIdentity competition, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ArcadeCompetitionAttemptRecord>>([]);
        public Task<IReadOnlyList<ArcadeCompetitionAttemptRecord>> LoadAttemptsForGameAsync(string gameId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ArcadeCompetitionAttemptRecord>>([]);
        public Task<IReadOnlyList<ArcadeCompetitionIdentity>> LoadDueSettlementCompetitionsAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ArcadeCompetitionIdentity>>([]);
        public Task<ArcadeCompetitionSettlementState> ReadSettlementStateAsync(ArcadeCompetitionIdentity competition, CancellationToken cancellationToken) =>
            Task.FromResult(new ArcadeCompetitionSettlementState(competition, null));
        public Task<ArcadeCompetitionSettlementState> StoreSettlementPlanAsync(ArcadeCompetitionPayoutPlan plan, DateTimeOffset createdAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ArcadeCompetitionSettlementState> MarkSettlementCompletedAsync(ArcadeCompetitionIdentity competition, DateTimeOffset completedAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new ArcadeCompetitionSettlementState(competition, completedAtUtc));
    }
}
