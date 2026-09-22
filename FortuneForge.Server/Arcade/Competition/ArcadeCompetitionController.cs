using System.Globalization;
using System.Collections.Immutable;
using FortuneForge.Games.Flappy;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Arcade.Asteroids;
using FortuneForge.Server.Arcade.Flappy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Arcade.Competition;

[ApiController]
[Route("api/arcade-competitions")]
public sealed class ArcadeCompetitionController : ControllerBase
{
    private readonly ArcadeCompetitionService service;
    private readonly IOptions<ArcadeCompetitionApiOptions> options;

    public ArcadeCompetitionController(ArcadeCompetitionService service, IOptions<ArcadeCompetitionApiOptions> options)
    {
        this.service = service;
        this.options = options;
    }

    [HttpGet("{gameId}/{period}")]
    public async Task<IActionResult> Get(string gameId, string period, [FromQuery] string? at, CancellationToken cancellationToken)
    {
        if (!options.Value.AllowedGameIds.Contains(gameId, StringComparer.Ordinal))
            return NotFound(new ArcadeCompetitionApiError("arcade-competition-game-not-found"));
        if (period == "all-time")
        {
            if (at is not null) return BadRequest(new ArcadeCompetitionApiError("arcade-competition-at-not-supported-for-all-time"));
            var allTime = await service.GetAllTimeSnapshotAsync(gameId, options.Value.AllTimeMaximumPlaces, cancellationToken);
            return Ok(new ArcadeCompetitionAllTimeResponse(
                allTime.GameId,
                "all-time",
                allTime.Leaderboard.Select(placement => new ArcadeCompetitionPlacementResponse(
                    placement.Position, placement.PlayerId, placement.Score)).ToArray()));
        }
        if (!TryParsePeriod(period, out var windowKind))
            return BadRequest(new ArcadeCompetitionApiError("arcade-competition-period-invalid"));
        if (!TryParseUtcTimestamp(at, out var atUtc))
            return BadRequest(new ArcadeCompetitionApiError("arcade-competition-at-invalid"));

        var competition = atUtc is null
            ? service.ResolveCurrentCompetition(gameId, windowKind)
            : service.ResolveCompetitionAt(gameId, windowKind, atUtc.Value);
        var snapshot = await service.GetSnapshotAsync(competition, cancellationToken);
        return Ok(new ArcadeCompetitionSnapshotResponse(
            competition.GameId,
            PeriodName(competition.WindowKind),
            competition.StartsAtUtc,
            competition.EndsAtUtc,
            snapshot.IsAcceptingAttempts,
            snapshot.SettlementState.IsCompleted,
            snapshot.TotalUniquePlayers,
            snapshot.PrizePlacements.Select(placement => new ArcadeCompetitionPlacementResponse(
                placement.Position, placement.PlayerId, placement.Score)).ToArray(),
            snapshot.Refunds.Select(refund => new ArcadeCompetitionRefundResponse(refund.PlayerId, refund.AmountCents)).SingleOrDefault(),
            snapshot.VisibleJackpotCents));
    }

    [HttpPost("{gameId}/{period}/attempts")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<IActionResult> StartAttempt(
        string gameId,
        string period,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromServices] AccountService accountService,
        [FromServices] ArcadeCompetitionAsteroidsPaidEntryService paidEntryService,
        CancellationToken cancellationToken)
    {
        if (!options.Value.AllowedGameIds.Contains(gameId, StringComparer.Ordinal))
            return NotFound(new ArcadeCompetitionApiError("arcade-competition-game-not-found"));
        if (!string.Equals(gameId, "asteroids", StringComparison.Ordinal))
            return NotFound(new ArcadeCompetitionApiError("arcade-competition-game-not-found"));
        if (period == "all-time")
            return BadRequest(new ArcadeCompetitionApiError("arcade-competition-period-not-startable"));
        if (!TryParsePeriod(period, out var windowKind))
            return BadRequest(new ArcadeCompetitionApiError("arcade-competition-period-invalid"));
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new ArcadeCompetitionApiError("arcade-competition-idempotency-key-required"));

        var account = (await accountService.GetProfileAsync(
            AccountSessionCookie.Read(Request),
            cancellationToken)).Value;
        if (account is null)
            return Unauthorized(new ArcadeCompetitionApiError("arcade-competition-authentication-required"));

        try
        {
            var result = await paidEntryService.StartAttemptAsync(
                idempotencyKey,
                account.UserId,
                windowKind,
                cancellationToken);
            var attempt = result.Attempt;
            return Ok(new ArcadeCompetitionPaidAttemptResponse(
                attempt.AttemptId,
                attempt.Competition.GameId,
                PeriodName(attempt.Competition.WindowKind),
                attempt.Competition.StartsAtUtc,
                attempt.Competition.EndsAtUtc,
                attempt.EntryFeeCents,
                result.WasAlreadyRecorded,
                result.Run.Run.RunId,
                result.Run.Run.Seed.ToString("x16")));
        }
        catch (Exception exception) { return PaidEntryFailure(exception); }
    }

    [HttpPost("{gameId}/{period}/runs/{runId}/replay")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<IActionResult> CompleteAsteroidsReplay(
        string gameId,
        string period,
        string runId,
        [FromBody] AsteroidsReplayInputRequest? request,
        [FromServices] AccountService accountService,
        [FromServices] ArcadeCompetitionAsteroidsPaidEntryService asteroidsService,
        CancellationToken cancellationToken)
    {
        if (!options.Value.AllowedGameIds.Contains(gameId, StringComparer.Ordinal) ||
            !string.Equals(gameId, "asteroids", StringComparison.Ordinal))
            return NotFound(new ArcadeCompetitionApiError("arcade-competition-game-not-found"));
        if (period == "all-time") return BadRequest(new ArcadeCompetitionApiError("arcade-competition-period-not-startable"));
        if (!TryParsePeriod(period, out var windowKind))
            return BadRequest(new ArcadeCompetitionApiError("arcade-competition-period-invalid"));
        if (!TryCreateReplay(request, out var replay))
            return BadRequest(new ArcadeCompetitionApiError("arcade-asteroids-replay-invalid"));

        var account = (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
        if (account is null)
            return Unauthorized(new ArcadeCompetitionApiError("arcade-competition-authentication-required"));
        try
        {
            var result = await asteroidsService.CompleteAttemptAsync(
                runId, account.UserId, windowKind, replay, cancellationToken);
            return Ok(new AsteroidsReplayCompletionResponse(
                runId,
                result.Score,
                result.Terminal.ToString().ToLowerInvariant(),
                result.WasAlreadyCompleted));
        }
        catch (Exception exception) { return ReplayCompletionFailure(exception); }
    }

    [HttpPost("asteroids/free/runs")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<IActionResult> StartFreeAsteroidsRun(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromServices] AccountService accountService,
        [FromServices] FirestoreAsteroidsFreeRunService freeRunService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new ArcadeCompetitionApiError("arcade-asteroids-free-idempotency-key-required"));

        var account = (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
        if (account is null)
            return Unauthorized(new ArcadeCompetitionApiError("arcade-competition-authentication-required"));
        try
        {
            var result = await freeRunService.StartAsync(idempotencyKey, account.UserId, cancellationToken);
            return Ok(new AsteroidsFreeRunStartResponse(
                result.Run.RunId,
                result.Run.Seed.ToString("x16"),
                result.StartedAtUtc,
                result.WasAlreadyStarted));
        }
        catch (Exception exception) { return FreeRunStartFailure(exception); }
    }

    [HttpPost("asteroids/free/runs/{runId}/replay")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<IActionResult> CompleteFreeAsteroidsReplay(
        string runId,
        [FromBody] AsteroidsReplayInputRequest? request,
        [FromServices] AccountService accountService,
        [FromServices] FirestoreAsteroidsFreeRunService freeRunService,
        CancellationToken cancellationToken)
    {
        if (!TryCreateReplay(request, out var replay))
            return BadRequest(new ArcadeCompetitionApiError("arcade-asteroids-replay-invalid"));
        var account = (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
        if (account is null)
            return Unauthorized(new ArcadeCompetitionApiError("arcade-competition-authentication-required"));
        try
        {
            var result = await freeRunService.CompleteAsync(runId, account.UserId, replay, cancellationToken);
            return Ok(new AsteroidsReplayCompletionResponse(
                result.RunId,
                result.Score,
                result.Terminal.ToString().ToLowerInvariant(),
                result.WasAlreadyCompleted));
        }
        catch (Exception exception) { return FreeRunFailure(exception); }
    }

    [HttpPost("flappy/free/runs")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<IActionResult> StartFreeFlappyRun(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromServices] AccountService accountService,
        [FromServices] FirestoreFlappyFreeRunService freeRunService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new ArcadeCompetitionApiError("arcade-flappy-free-idempotency-key-required"));

        var account = (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
        if (account is null)
            return Unauthorized(new ArcadeCompetitionApiError("arcade-competition-authentication-required"));
        try
        {
            var result = await freeRunService.StartAsync(idempotencyKey, account.UserId, cancellationToken);
            return Ok(new FlappyFreeRunStartResponse(
                result.Run.RunId,
                result.Run.Seed,
                result.StartedAtUtc,
                result.WasAlreadyStarted));
        }
        catch (Exception exception) { return FlappyFreeRunStartFailure(exception); }
    }

    [HttpPost("flappy/free/runs/{runId}/replay")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<IActionResult> CompleteFreeFlappyReplay(
        string runId,
        [FromBody] FlappyReplayInputRequest? request,
        [FromServices] AccountService accountService,
        [FromServices] FirestoreFlappyFreeRunService freeRunService,
        CancellationToken cancellationToken)
    {
        if (!TryCreateFlappyReplay(request, out var replay))
            return BadRequest(new ArcadeCompetitionApiError("arcade-flappy-replay-invalid"));
        var account = (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
        if (account is null)
            return Unauthorized(new ArcadeCompetitionApiError("arcade-competition-authentication-required"));
        try
        {
            var result = await freeRunService.CompleteAsync(runId, account.UserId, replay, cancellationToken);
            return Ok(new FlappyReplayCompletionResponse(
                result.RunId,
                result.Score,
                result.Terminal,
                result.WasAlreadyCompleted));
        }
        catch (Exception exception) { return FlappyFreeRunFailure(exception); }
    }

    private static bool TryParsePeriod(string value, out ArcadeCompetitionWindowKind kind)
    {
        switch (value)
        {
            case "daily": kind = ArcadeCompetitionWindowKind.Daily; return true;
            case "weekly": kind = ArcadeCompetitionWindowKind.Weekly; return true;
            default: kind = default; return false;
        }
    }

    private static bool TryParseUtcTimestamp(string? value, out DateTimeOffset? timestamp)
    {
        timestamp = null;
        if (value is null) return true;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) || parsed.Offset != TimeSpan.Zero)
            return false;
        timestamp = parsed;
        return true;
    }

    private static string PeriodName(ArcadeCompetitionWindowKind kind) => kind switch
    {
        ArcadeCompetitionWindowKind.Daily => "daily",
        ArcadeCompetitionWindowKind.Weekly => "weekly",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal static IActionResult PaidEntryFailure(Exception exception) => exception switch
    {
        ArcadeCompetitionInsufficientCreditsException => new ConflictObjectResult(
            new ArcadeCompetitionApiError("arcade-competition-insufficient-credits")),
        ArcadeCompetitionSettlementCompletedException => new ConflictObjectResult(
            new ArcadeCompetitionApiError("arcade-competition-settlement-closed")),
        ArgumentException => new BadRequestObjectResult(
            new ArcadeCompetitionApiError("arcade-competition-attempt-invalid")),
        InvalidOperationException => new ConflictObjectResult(
            new ArcadeCompetitionApiError("arcade-competition-attempt-conflict")),
        _ => new ObjectResult(new ArcadeCompetitionApiError("arcade-competition-attempt-failed"))
        {
            StatusCode = StatusCodes.Status500InternalServerError
        },
    };

    internal static IActionResult ReplayCompletionFailure(Exception exception) => exception switch
    {
        ArcadeCompetitionSettlementCompletedException => new ConflictObjectResult(
            new ArcadeCompetitionApiError("arcade-competition-settlement-closed")),
        ArgumentException => new BadRequestObjectResult(
            new ArcadeCompetitionApiError("arcade-asteroids-replay-invalid")),
        InvalidOperationException => new ConflictObjectResult(
            new ArcadeCompetitionApiError("arcade-asteroids-run-conflict")),
        _ => new ObjectResult(new ArcadeCompetitionApiError("arcade-asteroids-replay-failed"))
        {
            StatusCode = StatusCodes.Status500InternalServerError
        },
    };

    internal static IActionResult FreeRunFailure(Exception exception) => exception switch
    {
        ArgumentException => new BadRequestObjectResult(
            new ArcadeCompetitionApiError("arcade-asteroids-replay-invalid")),
        InvalidOperationException => new ConflictObjectResult(
            new ArcadeCompetitionApiError("arcade-asteroids-free-run-conflict")),
        _ => new ObjectResult(new ArcadeCompetitionApiError("arcade-asteroids-free-run-failed"))
        {
            StatusCode = StatusCodes.Status500InternalServerError
        },
    };

    internal static IActionResult FreeRunStartFailure(Exception exception) => exception switch
    {
        ArgumentException => new BadRequestObjectResult(
            new ArcadeCompetitionApiError("arcade-asteroids-free-run-invalid")),
        InvalidOperationException => new ConflictObjectResult(
            new ArcadeCompetitionApiError("arcade-asteroids-free-run-conflict")),
        _ => new ObjectResult(new ArcadeCompetitionApiError("arcade-asteroids-free-run-failed"))
        {
            StatusCode = StatusCodes.Status500InternalServerError
        },
    };

    internal static IActionResult FlappyFreeRunFailure(Exception exception) => exception switch
    {
        ArgumentException => new BadRequestObjectResult(
            new ArcadeCompetitionApiError("arcade-flappy-replay-invalid")),
        InvalidOperationException => new ConflictObjectResult(
            new ArcadeCompetitionApiError("arcade-flappy-free-run-conflict")),
        _ => new ObjectResult(new ArcadeCompetitionApiError("arcade-flappy-free-run-failed"))
        {
            StatusCode = StatusCodes.Status500InternalServerError
        },
    };

    internal static IActionResult FlappyFreeRunStartFailure(Exception exception) => exception switch
    {
        ArgumentException => new BadRequestObjectResult(
            new ArcadeCompetitionApiError("arcade-flappy-free-run-invalid")),
        InvalidOperationException => new ConflictObjectResult(
            new ArcadeCompetitionApiError("arcade-flappy-free-run-conflict")),
        _ => new ObjectResult(new ArcadeCompetitionApiError("arcade-flappy-free-run-failed"))
        {
            StatusCode = StatusCodes.Status500InternalServerError
        },
    };

    private static bool TryCreateReplay(AsteroidsReplayInputRequest? request, out AsteroidsReplay replay)
    {
        replay = null!;
        if (request?.Commands is null) return false;
        try
        {
            replay = new AsteroidsReplay(
                request.TotalSteps,
                request.Commands.Select(command => new AsteroidsInputCommand(
                    command.Step,
                    (AsteroidsInput)command.Input)).ToArray());
            AsteroidsReplayEvaluator.ValidateReplayInput(replay);
            return true;
        }
        catch (Exception) { return false; }
    }

    private static bool TryCreateFlappyReplay(FlappyReplayInputRequest? request, out FlappyReplay replay)
    {
        replay = null!;
        if (request?.FlapTicks is null) return false;
        try
        {
            replay = new FlappyReplay(request.TotalTicks, request.FlapTicks.ToImmutableArray());
            FirestoreFlappyFreeRunService.ValidateReplayInput(replay);
            return true;
        }
        catch (Exception) { return false; }
    }
}

public sealed record ArcadeCompetitionApiError(string Code);
public sealed record ArcadeCompetitionPaidAttemptResponse(
    string AttemptId,
    string GameId,
    string Period,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    long EntryFeeCents,
    bool WasReplay,
    string RunId,
    string SeedHex);
public sealed record AsteroidsReplayInputRequest(int TotalSteps, IReadOnlyList<AsteroidsReplayCommandRequest>? Commands);
public sealed record AsteroidsReplayCommandRequest(int Step, int Input);
public sealed record AsteroidsReplayCompletionResponse(string RunId, long Score, string Terminal, bool WasReplay);
public sealed record AsteroidsFreeRunStartResponse(string RunId, string SeedHex, DateTimeOffset StartedAtUtc, bool WasReplay);
public sealed record FlappyReplayInputRequest(int TotalTicks, IReadOnlyList<int>? FlapTicks);
public sealed record FlappyFreeRunStartResponse(string RunId, uint Seed, DateTimeOffset StartedAtUtc, bool WasReplay);
public sealed record FlappyReplayCompletionResponse(string RunId, long Score, string Terminal, bool WasReplay);
public sealed record ArcadeCompetitionPlacementResponse(int Position, string PlayerId, long Score);
public sealed record ArcadeCompetitionRefundResponse(string PlayerId, long AmountCents);
public sealed record ArcadeCompetitionAllTimeResponse(
    string GameId,
    string Period,
    IReadOnlyList<ArcadeCompetitionPlacementResponse> Leaderboard);
public sealed record ArcadeCompetitionSnapshotResponse(
    string GameId,
    string Period,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc,
    bool EntriesOpen,
    bool IsCompleted,
    int TotalUniquePlayers,
    IReadOnlyList<ArcadeCompetitionPlacementResponse> Leaderboard,
    ArcadeCompetitionRefundResponse? SolePlayerRefund,
    long VisibleJackpotCents);
