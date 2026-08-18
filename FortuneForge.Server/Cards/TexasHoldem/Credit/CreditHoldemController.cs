using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

[ApiController]
[Route("api/cards/texas-holdem/credit")]
public sealed class CreditHoldemController(
    IServiceProvider services,
    AccountService accountService,
    IConfiguration configuration,
    ILogger<CreditHoldemController> logger) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        return account is null
            ? Unauthorized(new { error = "Sign in to view credit Hold'em availability." })
            : Ok(StatusContract(configuration));
    }

    [HttpGet("session")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemReads)]
    public async Task<ActionResult> Session(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to play credit Hold'em." });
        try { return Ok((await Service.GetSessionAsync(account.UserId, cancellationToken)).Session); }
        catch (Exception exception) { return CreditHoldemHttp.FromException(this, exception, logger); }
    }

    [HttpPost("queue")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemWrites)]
    public async Task<ActionResult> Join(
        JoinCreditHoldemQueueRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to join credit Hold'em." });
        try
        {
            return Ok(ToMutation(await Service.JoinAsync(
                account.UserId, account.PlayerName, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return CreditHoldemHttp.FromException(this, exception, logger); }
    }

    [HttpDelete("queue/{ticketId}")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemWrites)]
    public async Task<ActionResult> Cancel(
        string ticketId,
        CreditHoldemVersionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to leave the Hold'em queue." });
        try
        {
            return Ok(ToMutation(await Service.CancelAsync(
                account.UserId, ticketId, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return CreditHoldemHttp.FromException(this, exception, logger); }
    }

    [HttpPost("matches/{matchId}/actions")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemWrites)]
    public async Task<ActionResult> Action(
        string matchId,
        CreditHoldemActionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to act at a credit Hold'em table." });
        try
        {
            return Ok(ToMutation(await Service.ActionAsync(
                account.UserId, matchId, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return CreditHoldemHttp.FromException(this, exception, logger); }
    }

    [HttpPost("matches/{matchId}/next-hand")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemWrites)]
    public async Task<ActionResult> NextHand(
        string matchId,
        CreditHoldemVersionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to deal the next Hold'em hand." });
        try
        {
            return Ok(ToMutation(await Service.NextHandAsync(
                account.UserId, matchId, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return CreditHoldemHttp.FromException(this, exception, logger); }
    }

    [HttpPost("matches/{matchId}/leave")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemWrites)]
    public async Task<ActionResult> Leave(
        string matchId,
        CreditHoldemVersionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to leave the Hold'em table." });
        try
        {
            return Ok(ToMutation(await Service.LeaveAsync(
                account.UserId, matchId, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return CreditHoldemHttp.FromException(this, exception, logger); }
    }

    [HttpGet("history")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemReads)]
    public async Task<ActionResult> History([FromQuery] int limit = 30, CancellationToken cancellationToken = default)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to view Hold'em history." });
        try { return Ok(await Service.HistoryAsync(account.UserId, limit, cancellationToken)); }
        catch (Exception exception) { return CreditHoldemHttp.FromException(this, exception, logger); }
    }

    [HttpPost("history/{eventId}/seen")]
    [EnableRateLimiting(RateLimitPolicies.CreditHoldemWrites)]
    public async Task<ActionResult> MarkHistorySeen(string eventId, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to update Hold'em history." });
        try { return Ok(await Service.MarkHistorySeenAsync(account.UserId, eventId, cancellationToken)); }
        catch (Exception exception) { return CreditHoldemHttp.FromException(this, exception, logger); }
    }

    internal static bool IsEnabled(IConfiguration source) =>
        source.GetValue("Features:CreditTexasHoldemEnabled", false);

    internal static CreditHoldemStatusResponse StatusContract(IConfiguration configuration) => new(
        true,
        CreditHoldemMoney.MinimumStartPlayers,
        CreditHoldemMoney.MaximumSeats,
        configuration.GetValue($"{CreditHoldemOptions.SectionName}:AllowSingleHumanBotFill", false) ? 1 : 2,
        CreditHoldemMoney.ToCredits(CreditHoldemMoney.SmallBlindCents),
        CreditHoldemMoney.ToCredits(CreditHoldemMoney.BigBlindCents),
        checked((int)CreditHoldemEngine.ActionDuration.TotalSeconds),
        checked((int)CreditHoldemEngine.MatchDuration.TotalSeconds),
        CreditHoldemTableRules.All.Select(rule => rule.Public).ToArray());

    private CreditHoldemService Service => services.GetRequiredService<CreditHoldemService>();
    private ActionResult? Disabled() => IsEnabled(configuration) ? null : StatusCode(
        StatusCodes.Status503ServiceUnavailable,
        new
        {
            code = "credit-texas-holdem-disabled",
            error = "Credit Texas Hold'em is disabled while its accounting controls are being verified."
        });
    private async Task<AccountSummary?> AccountAsync(CancellationToken cancellationToken) =>
        (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
    private static CreditHoldemMutationResponse ToMutation(CreditHoldemStoreResult value) =>
        new(value.Session, CreditHoldemMoney.ToCredits(value.BalanceCents));
}

internal static class CreditHoldemHttp
{
    public static ActionResult FromException(ControllerBase controller, Exception exception, ILogger logger)
    {
        switch (exception)
        {
            case CreditHoldemNotFoundException:
                return controller.NotFound(new { error = exception.Message });
            case CreditHoldemInsufficientCreditsException insufficient:
                return controller.Conflict(new
                {
                    code = "insufficient-slot-credits",
                    error = insufficient.Message,
                    available = insufficient.Available,
                    required = insufficient.Required
                });
            case CreditHoldemConflictException:
                return controller.Conflict(new { code = "credit-holdem-state-conflict", error = exception.Message });
            case CreditHoldemIllegalActionException:
                return controller.BadRequest(new { code = "illegal-holdem-action", error = exception.Message });
            case ArgumentException:
                return controller.BadRequest(new { error = exception.Message });
            default:
                logger.LogError(exception, "Credit Hold'em request failed; trace {TraceIdentifier}.", controller.HttpContext.TraceIdentifier);
                return controller.Problem(
                    "Credit Hold'em could not complete the request.",
                    statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
