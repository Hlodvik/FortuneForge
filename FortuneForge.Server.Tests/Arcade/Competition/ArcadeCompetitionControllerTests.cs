using System.Text.Json;
using FortuneForge.Server.Arcade.Competition;
using FortuneForge.Server.Arcade.Flappy;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
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
    public async Task PaidAttemptRouteGuardsGamePeriodAndIdempotencyBeforeResolvingServices()
    {
        var controller = Create();

        var unknownGame = Assert.IsType<NotFoundObjectResult>(await controller.StartAttempt(
            "other-arcade", "daily", "attempt-1", null!, null!, default));
        var allTime = Assert.IsType<BadRequestObjectResult>(await controller.StartAttempt(
            "asteroids", "all-time", "attempt-1", null!, null!, default));
        var badPeriod = Assert.IsType<BadRequestObjectResult>(await controller.StartAttempt(
            "asteroids", "monthly", "attempt-1", null!, null!, default));
        var missingKey = Assert.IsType<BadRequestObjectResult>(await controller.StartAttempt(
            "asteroids", "daily", " ", null!, null!, default));

        Assert.Equal("arcade-competition-game-not-found", ErrorCode(unknownGame));
        Assert.Equal("arcade-competition-period-not-startable", ErrorCode(allTime));
        Assert.Equal("arcade-competition-period-invalid", ErrorCode(badPeriod));
        Assert.Equal("arcade-competition-idempotency-key-required", ErrorCode(missingKey));
    }

    [Fact]
    public void PaidAttemptContractIsPostAndUsesThePaidPlayRateLimit()
    {
        var method = typeof(ArcadeCompetitionController).GetMethod(nameof(ArcadeCompetitionController.StartAttempt))
            ?? throw new InvalidOperationException("Paid attempt action is missing.");

        Assert.Equal("{gameId}/{period}/attempts", method.GetCustomAttributes(typeof(HttpPostAttribute), false)
            .Cast<HttpPostAttribute>().Single().Template);
        Assert.Equal(RateLimitPolicies.SlotSpins, method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false)
            .Cast<EnableRateLimitingAttribute>().Single().PolicyName);
        Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType.Name == "ArcadeCompetitionAsteroidsPaidEntryService");
        Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType.Name == "ArcadeCompetitionPaidEntryService");
        Assert.DoesNotContain(method.GetParameters(), parameter => parameter.Name is "score" or "userId" or "balance" or "time" or "window");
    }

    [Fact]
    public void PaidAttemptResponseExposesTheRunSeedOnlyAsCanonicalHex()
    {
        var response = new ArcadeCompetitionPaidAttemptResponse(
            "attempt-1",
            "asteroids",
            "daily",
            new DateTimeOffset(2026, 9, 3, 22, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 4, 22, 0, 0, TimeSpan.Zero),
            100,
            false,
            "asteroids_0123456789abcdef",
            "000000000000002a");
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"seedHex\":\"000000000000002a\"", json);
        Assert.DoesNotContain("\"seed\":", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("42", json);
    }

    [Fact]
    public void ReplayCompletionContractIsPaidPostAndExposesOnlyReplayInputs()
    {
        var method = typeof(ArcadeCompetitionController).GetMethod(nameof(ArcadeCompetitionController.CompleteAsteroidsReplay))
            ?? throw new InvalidOperationException("Replay completion action is missing.");
        Assert.Equal("{gameId}/{period}/runs/{runId}/replay", method.GetCustomAttributes(typeof(HttpPostAttribute), false)
            .Cast<HttpPostAttribute>().Single().Template);
        Assert.Equal(RateLimitPolicies.SlotSpins, method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false)
            .Cast<EnableRateLimitingAttribute>().Single().PolicyName);
        Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType.Name == "AccountService");
        Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType.Name == "ArcadeCompetitionAsteroidsPaidEntryService");
        Assert.DoesNotContain(method.GetParameters(), parameter => parameter.Name is "score" or "userId" or "seed" or "time" or "competition");
        var properties = typeof(AsteroidsReplayInputRequest).GetProperties().Select(property => property.Name).ToArray();
        Assert.Equal(new[] { "TotalSteps", "Commands" }, properties);
    }

    [Theory]
    [InlineData("settled", 409, "arcade-competition-settlement-closed")]
    [InlineData("invalid", 400, "arcade-asteroids-replay-invalid")]
    [InlineData("conflict", 409, "arcade-asteroids-run-conflict")]
    [InlineData("unexpected", 500, "arcade-asteroids-replay-failed")]
    public void ReplayCompletionFailuresHaveStableSafeCodes(string kind, int status, string code)
    {
        var result = ArcadeCompetitionController.ReplayCompletionFailure(kind switch
        {
            "settled" => new ArcadeCompetitionSettlementCompletedException(),
            "invalid" => new ArgumentException("internal validation"),
            "conflict" => new InvalidOperationException("internal conflict"),
            _ => new Exception("internal failure"),
        });
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(status, objectResult.StatusCode);
        Assert.Equal(code, Assert.IsType<ArcadeCompetitionApiError>(objectResult.Value).Code);
    }

    [Fact]
    public void ReplayCompletionResponseSerializesOnlySafeDerivedFields()
    {
        var json = JsonSerializer.Serialize(new AsteroidsReplayCompletionResponse(
            "asteroids_0123456789abcdef", 100, "completed", false), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"score\":100", json);
        Assert.DoesNotContain("seed", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("player", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FreeStartRequiresIdempotencyBeforeResolvingAuthenticatedServices()
    {
        var result = Assert.IsType<BadRequestObjectResult>(await Create().StartFreeAsteroidsRun(
            " ", null!, null!, default));

        Assert.Equal("arcade-asteroids-free-idempotency-key-required", ErrorCode(result));
    }

    [Fact]
    public void FreeRunRoutesAreAuthenticatedRateLimitedReplayOnlyContracts()
    {
        var start = typeof(ArcadeCompetitionController).GetMethod(nameof(ArcadeCompetitionController.StartFreeAsteroidsRun))
            ?? throw new InvalidOperationException("Free start action is missing.");
        var complete = typeof(ArcadeCompetitionController).GetMethod(nameof(ArcadeCompetitionController.CompleteFreeAsteroidsReplay))
            ?? throw new InvalidOperationException("Free completion action is missing.");

        Assert.Equal("asteroids/free/runs", start.GetCustomAttributes(typeof(HttpPostAttribute), false)
            .Cast<HttpPostAttribute>().Single().Template);
        Assert.Equal("asteroids/free/runs/{runId}/replay", complete.GetCustomAttributes(typeof(HttpPostAttribute), false)
            .Cast<HttpPostAttribute>().Single().Template);
        Assert.All(new[] { start, complete }, method =>
        {
            Assert.Equal(RateLimitPolicies.SlotSpins, method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false)
                .Cast<EnableRateLimitingAttribute>().Single().PolicyName);
            Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType.Name == "AccountService");
            Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType.Name == "FirestoreAsteroidsFreeRunService");
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.Name is "score" or "userId" or "seed" or "balance");
        });
    }

    [Fact]
    public void FlappyFreeRunRoutesAreAuthenticatedRateLimitedReplayOnlyContracts()
    {
        var start = typeof(ArcadeCompetitionController).GetMethod(nameof(ArcadeCompetitionController.StartFreeFlappyRun))
            ?? throw new InvalidOperationException("Flappy free start action is missing.");
        var complete = typeof(ArcadeCompetitionController).GetMethod(nameof(ArcadeCompetitionController.CompleteFreeFlappyReplay))
            ?? throw new InvalidOperationException("Flappy free completion action is missing.");

        Assert.Equal("flappy/free/runs", start.GetCustomAttributes(typeof(HttpPostAttribute), false)
            .Cast<HttpPostAttribute>().Single().Template);
        Assert.Equal("flappy/free/runs/{runId}/replay", complete.GetCustomAttributes(typeof(HttpPostAttribute), false)
            .Cast<HttpPostAttribute>().Single().Template);
        Assert.All(new[] { start, complete }, method =>
        {
            Assert.Equal(RateLimitPolicies.SlotSpins, method.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false)
                .Cast<EnableRateLimitingAttribute>().Single().PolicyName);
            Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType.Name == "AccountService");
            Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType.Name == "FirestoreFlappyFreeRunService");
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.Name is "score" or "userId" or "seed" or "balance");
        });
        Assert.Equal(new[] { "TotalTicks", "FlapTicks" }, typeof(FlappyReplayInputRequest).GetProperties().Select(property => property.Name));
    }

    [Theory]
    [InlineData("insufficient", 409, "arcade-competition-insufficient-credits")]
    [InlineData("settled", 409, "arcade-competition-settlement-closed")]
    [InlineData("argument", 400, "arcade-competition-attempt-invalid")]
    [InlineData("conflict", 409, "arcade-competition-attempt-conflict")]
    [InlineData("unexpected", 500, "arcade-competition-attempt-failed")]
    public void PaidAttemptFailuresHaveStableSafeCodes(string kind, int expectedStatus, string expectedCode)
    {
        var result = ArcadeCompetitionController.PaidEntryFailure(kind switch
        {
            "insufficient" => new ArcadeCompetitionInsufficientCreditsException(0, 100),
            "settled" => new ArcadeCompetitionSettlementCompletedException(),
            "argument" => new ArgumentException("internal validation detail"),
            "conflict" => new InvalidOperationException("internal conflict detail"),
            _ => new Exception("internal failure detail"),
        });

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        Assert.Equal(expectedCode, Assert.IsType<ArcadeCompetitionApiError>(objectResult.Value).Code);
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
