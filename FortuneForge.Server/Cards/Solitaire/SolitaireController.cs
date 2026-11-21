using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.FreeSolitaire.Server;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.GameWorkers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.Solitaire;


[ApiController]
[Route("api/solitaire")]
public sealed class SolitaireController(
    AccountService accountService,
    IConfiguration configuration,
    FirestoreSolitaireFreeRunService freeRunService,
    ILogger<SolitaireController> logger) : ControllerBase
{
    [HttpPost("free/runs")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> StartFreeRun(
        SolitaireFreeRunStartRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to play free Solitaire." });
        try
        {
            return Ok(await freeRunService.StartAsync(
                idempotencyKey ?? string.Empty, account.UserId, request.DrawCount, cancellationToken,
                request.ReplayRunId, request.GameId ?? FirestoreSolitaireFreeRunService.LegacyGameId));
        }
        catch (Exception exception)
        {
            return FreeRunHttp(exception);
        }
    }

    [HttpPost("free/runs/{runId}/replay")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> CompleteFreeRun(
        string runId,
        SolitaireFreeRunReplayRequest request,
        CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to submit free Solitaire." });
        try
        {
            return Ok(await freeRunService.CompleteAsync(
                runId, account.UserId, request.Commands ?? [], cancellationToken));
        }
        catch (Exception exception)
        {
            return FreeRunHttp(exception);
        }
    }

    [HttpPost("free/runs/{runId}/pause")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> PauseFreeRun(string runId, CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to pause free Solitaire." });
        try
        {
            return Ok(await freeRunService.PauseAsync(runId, account.UserId, cancellationToken));
        }
        catch (Exception exception)
        {
            return FreeRunHttp(exception);
        }
    }

    [HttpPost("free/runs/{runId}/resume")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> ResumeFreeRun(string runId, CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to resume free Solitaire." });
        try
        {
            return Ok(await freeRunService.ResumeAsync(runId, account.UserId, cancellationToken));
        }
        catch (Exception exception)
        {
            return FreeRunHttp(exception);
        }
    }

    [HttpGet("free/runs/{runId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> GetFreeRun(string runId, CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to reopen free Solitaire." });
        try { return Ok(await freeRunService.GetAsync(runId, account.UserId, cancellationToken)); }
        catch (Exception exception) { return FreeRunHttp(exception); }
    }

    [HttpGet("session")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Session(CancellationToken cancellationToken) => CompetitiveUnavailableAsync();

    [HttpPost("queue")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Join(
        JoinSolitaireQueueRequest request,
        CancellationToken cancellationToken) => CompetitiveUnavailableAsync();

    [HttpDelete("queue/{ticketId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Cancel(
        string ticketId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) => CompetitiveUnavailableAsync();

    [HttpPost("matches/{matchId}/commands")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Command(
        string matchId,
        SolitaireCommandRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) => CompetitiveUnavailableAsync();

    [HttpPost("matches/{matchId}/forfeit")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Forfeit(
        string matchId,
        SolitaireForfeitRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) => CompetitiveUnavailableAsync();

    [HttpPost("matches/{matchId}/dismiss")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Dismiss(
        string matchId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) => CompetitiveUnavailableAsync();

    [HttpPost("matches/{matchId}/claim")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Claim(
        string matchId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) => CompetitiveUnavailableAsync();

    [HttpGet("history")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> History(
        [FromQuery] int limit = 30,
        CancellationToken cancellationToken = default) => CompetitiveUnavailableAsync();

    [HttpGet("history-page")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> HistoryPage(
        [FromQuery] int limit = 30,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) => CompetitiveUnavailableAsync();

    private ActionResult FreeRunHttp(Exception exception) => exception switch
    {
        GameWorkerUnavailableException => StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = exception.Message }),
        ArgumentException => BadRequest(new { error = exception.Message }),
        InvalidOperationException => Conflict(new { error = exception.Message }),
        _ => SolitaireHttp.FromException(this, exception, logger),
    };

    private Task<ActionResult> CompetitiveUnavailableAsync() => Task.FromResult<ActionResult>(IsEnabled(configuration)
        ? StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            code = "solitaire-worker-required",
            error = "Competitive Solitaire requires its game backend. Retry the original action when it is available."
        })
        : StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            code = "competitive-solitaire-disabled",
            error = "Competitive Solitaire is still being verified and cannot accept a buy-in yet."
        }));

    internal static bool IsEnabled(IConfiguration source) =>
        source.GetValue("Features:CompetitiveSolitaireEnabled", false);

    internal static bool IsSingleHumanBotFillEnabled(IConfiguration source) =>
        source.GetValue(
            $"{CompetitiveSolitaireOptions.SectionName}:AllowSingleHumanBotFill",
            false);

    private async Task<AccountSummary?> AccountAsync(CancellationToken cancellationToken) =>
        (await accountService.GetProfileAsync(
            AccountSessionCookie.Read(Request),
            cancellationToken)).Value;

}

internal static class SolitaireHttp
{
    public static ActionResult FromException(
        ControllerBase controller,
        Exception exception,
        ILogger logger)
    {
        switch (exception)
        {
            case GameWorkerUnavailableException:
                return controller.StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { code = "solitaire-unavailable", error = exception.Message });
            case GameWalletUnavailableException or GameWalletConflictException or ManagedPlayerRosterUnavailableException
                or SolitaireFinancialPendingException or SolitaireAdmissionPausedException or Grpc.Core.RpcException:
                return controller.StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { code = "solitaire-unavailable", error = "Competitive Solitaire could not confirm the request." });
            case SolitaireNotFoundException:
                return controller.NotFound(new { error = exception.Message });
            case SolitaireInsufficientCreditsException insufficient:
                return controller.Conflict(new
                {
                    code = "insufficient-slot-credits",
                    error = insufficient.Message,
                    available = insufficient.Available,
                    required = insufficient.Required
                });
            case SolitaireConflictException:
                return controller.Conflict(new
                {
                    code = "solitaire-state-conflict",
                    error = exception.Message
                });
            case SolitaireIllegalMoveException:
                return controller.BadRequest(new
                {
                    code = "illegal-solitaire-move",
                    error = exception.Message
                });
            case ArgumentException:
                return controller.BadRequest(new { error = exception.Message });
            default:
                logger.LogError(
                    exception,
                    "Competitive Solitaire request failed; trace {TraceIdentifier}.",
                    controller.HttpContext.TraceIdentifier);
                return controller.Problem(
                    "Competitive Solitaire could not complete the request.",
                    statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
