using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.Solitaire;

public sealed record SolitaireForfeitRequest(int ExpectedVersion);

[ApiController]
[Route("api/solitaire")]
public sealed class SolitaireController(
    FirestoreDb database,
    AccountService accountService,
    IConfiguration configuration,
    ILogger<SolitaireController> logger) : ControllerBase
{
    [HttpGet("session")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Session(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized(new { error = "Sign in to play competitive Solitaire." });
        }
        try
        {
            return Ok((await Service().GetSessionAsync(account.UserId, cancellationToken)).Session);
        }
        catch (Exception exception)
        {
            return SolitaireHttp.FromException(this, exception, logger);
        }
    }

    [HttpPost("queue")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Join(
        JoinSolitaireQueueRequest request,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized(new { error = "Sign in to join competitive Solitaire." });
        }
        try
        {
            return Ok(ToMutation(await Service().JoinAsync(
                account.UserId,
                account.PlayerName,
                request,
                cancellationToken)));
        }
        catch (Exception exception)
        {
            return SolitaireHttp.FromException(this, exception, logger);
        }
    }

    [HttpDelete("queue/{ticketId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Cancel(
        string ticketId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized(new { error = "Sign in to cancel a Solitaire queue ticket." });
        }
        try
        {
            return Ok(ToMutation(await Service().CancelAsync(
                account.UserId,
                ticketId,
                idempotencyKey ?? string.Empty,
                cancellationToken)));
        }
        catch (Exception exception)
        {
            return SolitaireHttp.FromException(this, exception, logger);
        }
    }

    [HttpPost("matches/{matchId}/commands")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Command(
        string matchId,
        SolitaireCommandRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized(new { error = "Sign in to play competitive Solitaire." });
        }
        try
        {
            return Ok(ToMutation(await Service().CommandAsync(
                account.UserId,
                matchId,
                request,
                idempotencyKey ?? string.Empty,
                cancellationToken)));
        }
        catch (Exception exception)
        {
            return SolitaireHttp.FromException(this, exception, logger);
        }
    }

    [HttpPost("matches/{matchId}/forfeit")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Forfeit(
        string matchId,
        SolitaireForfeitRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized(new { error = "Sign in to forfeit a Solitaire match." });
        }
        try
        {
            return Ok(ToMutation(await Service().ForfeitAsync(
                account.UserId,
                matchId,
                request.ExpectedVersion,
                idempotencyKey ?? string.Empty,
                cancellationToken)));
        }
        catch (Exception exception)
        {
            return SolitaireHttp.FromException(this, exception, logger);
        }
    }

    [HttpPost("matches/{matchId}/dismiss")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Dismiss(
        string matchId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized(new { error = "Sign in to dismiss a Solitaire result." });
        }
        try
        {
            return Ok(ToMutation(await Service().DismissAsync(
                account.UserId,
                matchId,
                idempotencyKey ?? string.Empty,
                cancellationToken)));
        }
        catch (Exception exception)
        {
            return SolitaireHttp.FromException(this, exception, logger);
        }
    }

    [HttpPost("matches/{matchId}/claim")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Claim(
        string matchId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized(new { error = "Sign in to claim a Solitaire result." });
        }
        try
        {
            return Ok(ToMutation(await Service().ClaimAsync(
                account.UserId,
                matchId,
                idempotencyKey ?? string.Empty,
                cancellationToken)));
        }
        catch (Exception exception)
        {
            return SolitaireHttp.FromException(this, exception, logger);
        }
    }

    [HttpGet("history")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> History(
        [FromQuery] int limit = 30,
        CancellationToken cancellationToken = default)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized(new { error = "Sign in to read Solitaire history." });
        }
        try
        {
            return Ok(await Service().GetHistoryAsync(account.UserId, limit, cancellationToken));
        }
        catch (Exception exception)
        {
            return SolitaireHttp.FromException(this, exception, logger);
        }
    }

    private CompetitiveSolitaireService Service() =>
        new(new FirestoreCompetitiveSolitaireStore(database, new CompetitiveSolitaireOptions
        {
            AllowSingleHumanBotFill = IsSingleHumanBotFillEnabled(configuration)
        }));

    private ActionResult? Disabled() => IsEnabled(configuration)
        ? null
        : StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            code = "competitive-solitaire-disabled",
            error = "Competitive Solitaire is still being verified and cannot accept a buy-in yet."
        });

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

    private static SolitaireMutationResponse ToMutation(SolitaireStoreSession value) =>
        new(value.Session, SolitaireMoney.ToCredits(value.BalanceCents));
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
