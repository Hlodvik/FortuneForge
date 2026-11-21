using System.Globalization;
using FortuneForge.Games.HorseFlight;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Arcade.HorseFlight;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Arcade.Competition;

[ApiController]
[Route(ArcadeCompetitionController.RoutePrefix)]
public sealed class ArcadeCompetitionController : ControllerBase
{
    internal const string RoutePrefix = "api/arcade-competitions";
    private const string StandingsRoute = "{gameId}/{period}";
    internal const string PublicStandingsRoutePattern = RoutePrefix + "/" + StandingsRoute;

    private readonly ArcadeCompetitionService service;
    private readonly IOptions<ArcadeCompetitionApiOptions> options;

    public ArcadeCompetitionController(ArcadeCompetitionService service, IOptions<ArcadeCompetitionApiOptions> options)
    {
        this.service = service;
        this.options = options;
    }

    [HttpGet(StandingsRoute)]
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

    [HttpGet("{gameId}/{period}/runs/{runId}")]
    public IActionResult GetRun(string gameId, string period)
    {
        if (!options.Value.AllowedGameIds.Contains(gameId, StringComparer.Ordinal))
            return NotFound(new ArcadeCompetitionApiError("arcade-competition-game-not-found"));
        if (!TryParsePeriod(period, out _) && !(gameId == "asteroids" && period == "free"))
            return BadRequest(new ArcadeCompetitionApiError("arcade-competition-period-invalid"));
        return WorkerRequired();
    }

    [HttpPost("{gameId}/{period}/attempts")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public IActionResult StartAttempt(string gameId, string period, [FromServices] IConfiguration configuration)
    {
        if (!options.Value.AllowedGameIds.Contains(gameId, StringComparer.Ordinal))
            return NotFound(new ArcadeCompetitionApiError("arcade-competition-game-not-found"));
        if (!TryParsePeriod(period, out _))
            return BadRequest(new ArcadeCompetitionApiError("arcade-competition-period-invalid"));
        return gameId == "horse-flight" ? HorseFlightWorkerRequired(configuration) : WorkerRequired();
    }

    [HttpPost("{gameId}/{period}/runs/{runId}/replay")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public IActionResult CompletePaidReplay(string gameId, string period)
    {
        if (!options.Value.AllowedGameIds.Contains(gameId, StringComparer.Ordinal))
            return NotFound(new ArcadeCompetitionApiError("arcade-competition-game-not-found"));
        if (!TryParsePeriod(period, out _))
            return BadRequest(new ArcadeCompetitionApiError("arcade-competition-period-invalid"));
        return WorkerRequired();
    }

    [HttpPost("asteroids/free/runs")]
    [HttpPost("asteroids/free/runs/{runId}/replay")]
    [HttpPost("flappy/free/runs")]
    [HttpPost("flappy/free/runs/{runId}/replay")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public IActionResult FreeRun() => WorkerRequired();

    [HttpPost("horse-flight/free/runs")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public IActionResult StartFreeHorseFlightRun([FromServices] IConfiguration configuration) =>
        HorseFlightWorkerRequired(configuration);

    [HttpPost("horse-flight/free/runs/{runId}/replay")]
    [HttpPost("horse-flight/{period}/runs/{runId}/replay")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    [RequestSizeLimit(CompleteHorseFlightRunRequest.MaximumBodyBytes)]
    public IActionResult CompleteHorseFlightReplay([FromServices] IConfiguration configuration) =>
        HorseFlightWorkerRequired(configuration);

    // These routes have no embedded execution path when forwarding is unavailable.
    private ObjectResult WorkerRequired() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new ArcadeCompetitionApiError("arcade-competition-worker-required"));

    private ObjectResult HorseFlightWorkerRequired(IConfiguration configuration) =>
        HorseFlightController.IsEnabled(configuration) ? WorkerRequired() :
            StatusCode(StatusCodes.Status503ServiceUnavailable, new ArcadeCompetitionApiError("arcade-horse-flight-disabled"));

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
}

public sealed record ArcadeCompetitionApiError(string Code);

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
