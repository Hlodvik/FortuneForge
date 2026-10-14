using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

[ApiController]
[Route("api/cards/three-card-poker/table")]
public sealed class ThreeCardPokerTableController(
    IServiceProvider services,
    AccountService accountService,
    IConfiguration configuration,
    ILogger<ThreeCardPokerTableController> logger) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        return account is null
            ? Unauthorized(new { error = "Sign in to view Three Card Poker table availability." })
            : Ok(StatusContract());
    }

    [HttpGet("session")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableReads)]
    public async Task<ActionResult> Session(CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to play Three Card Poker table mode." });
        if (Disabled() is { } unavailable) return unavailable;
        try { return Ok((await Service.GetSessionAsync(account.UserId, cancellationToken)).Session); }
        catch (Exception exception) { return ThreeCardPokerTableHttp.FromException(this, exception, logger); }
    }

    [HttpGet("snapshot")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableReads)]
    public async Task<ActionResult> Snapshot(CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to play Three Card Poker table mode." });
        if (Disabled() is { } unavailable) return unavailable;
        try { return Ok(ToMutation(await Service.GetSessionAsync(account.UserId, cancellationToken))); }
        catch (Exception exception) { return ThreeCardPokerTableHttp.FromException(this, exception, logger); }
    }

    [HttpGet("history")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableReads)]
    public async Task<ActionResult> History(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to view Three Card Poker table history." });
        if (Disabled() is { } unavailable) return unavailable;
        try { return Ok(await Service.GetHistoryAsync(account.UserId, limit, cancellationToken)); }
        catch (Exception exception) { return ThreeCardPokerTableHttp.FromException(this, exception, logger); }
    }

    [HttpPost("history/{resultId}/seen")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableWrites)]
    public async Task<ActionResult> MarkHistorySeen(string resultId, CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to update Three Card Poker table history." });
        if (Disabled() is { } unavailable) return unavailable;
        try { return Ok(await Service.MarkHistorySeenAsync(account.UserId, resultId, cancellationToken)); }
        catch (Exception exception) { return ThreeCardPokerTableHttp.FromException(this, exception, logger); }
    }

    [HttpPost("queue")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableWrites)]
    public async Task<ActionResult> Join(
        JoinThreeCardPokerTableQueueRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to join a Three Card Poker table." });
        if (Disabled() is { } unavailable) return unavailable;
        try
        {
            return Ok(ToMutation(await Service.JoinAsync(
                account.UserId, account.PlayerName, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return ThreeCardPokerTableHttp.FromException(this, exception, logger); }
    }

    [HttpDelete("queue/{ticketId}")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableWrites)]
    public async Task<ActionResult> Cancel(
        string ticketId,
        ThreeCardPokerTableVersionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to leave the Three Card Poker queue." });
        if (Disabled() is { } unavailable) return unavailable;
        try
        {
            return Ok(ToMutation(await Service.CancelAsync(
                account.UserId, ticketId, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return ThreeCardPokerTableHttp.FromException(this, exception, logger); }
    }

    [HttpPost("tables/{tableId}/wagers")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableWrites)]
    public async Task<ActionResult> Wager(
        string tableId,
        ThreeCardPokerTableWagerRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to wager at a Three Card Poker table." });
        if (Disabled() is { } unavailable) return unavailable;
        try
        {
            return Ok(ToMutation(await Service.WagerAsync(
                account.UserId, tableId, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return ThreeCardPokerTableHttp.FromException(this, exception, logger); }
    }

    [HttpPost("tables/{tableId}/actions")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableWrites)]
    public async Task<ActionResult> Action(
        string tableId,
        ThreeCardPokerTableActionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to act at a Three Card Poker table." });
        if (Disabled() is { } unavailable) return unavailable;
        try
        {
            return Ok(ToMutation(await Service.ActionAsync(
                account.UserId, tableId, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return ThreeCardPokerTableHttp.FromException(this, exception, logger); }
    }

    [HttpPost("tables/{tableId}/sit-out")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableWrites)]
    public async Task<ActionResult> SitOut(
        string tableId,
        ThreeCardPokerTableVersionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to sit out a Three Card Poker round." });
        if (Disabled() is { } unavailable) return unavailable;
        try
        {
            return Ok(ToMutation(await Service.SitOutAsync(
                account.UserId, tableId, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return ThreeCardPokerTableHttp.FromException(this, exception, logger); }
    }

    [HttpPost("tables/{tableId}/leave")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableWrites)]
    public async Task<ActionResult> Leave(
        string tableId,
        ThreeCardPokerTableVersionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to leave a Three Card Poker table." });
        if (Disabled() is { } unavailable) return unavailable;
        try
        {
            return Ok(ToMutation(await Service.LeaveAsync(
                account.UserId, tableId, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return ThreeCardPokerTableHttp.FromException(this, exception, logger); }
    }

    internal static bool IsEnabled(IConfiguration source) =>
        source.GetValue("Features:ThreeCardPokerTableEnabled", false);

    internal static ThreeCardPokerTableStatusResponse StatusContract() => new(
        true,
        ThreeCardPokerMoney.ToRand(ThreeCardPokerMoney.MinimumWagerCents),
        ThreeCardPokerMoney.ToRand(ThreeCardPokerMoney.MaximumWagerCents),
        ThreeCardPokerMoney.ToRand(ThreeCardPokerMoney.WagerIncrementCents),
        1,
        1,
        0,
        0,
        "Dealer qualifies with queen high or better; Play equals Ante", true,
        "Straight 1:1; Three of a kind 4:1; Straight flush 5:1",
        "Pair 1:1; Flush 3:1; Straight 6:1; Three of a kind 30:1; Straight flush 40:1", true);

    private ActionResult? Disabled() => IsEnabled(configuration) ? null : StatusCode(
        StatusCodes.Status503ServiceUnavailable,
        new
        {
            code = "three-card-poker-table-disabled",
            error = "Three Card Poker table mode is currently unavailable."
        });

    private async Task<AccountSummary?> AccountAsync(CancellationToken cancellationToken) =>
        (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;

    private ThreeCardPokerTableService Service => services.GetRequiredService<ThreeCardPokerTableService>();

    private static ThreeCardPokerTableMutationResponse ToMutation(ThreeCardPokerTableStoreResult value) =>
        new(value.Session, ThreeCardPokerMoney.ToRand(value.BalanceCents));
}

internal static class ThreeCardPokerTableHttp
{
    public static ActionResult FromException(ControllerBase controller, Exception exception, ILogger logger)
    {
        switch (exception)
        {
            case ThreeCardPokerTableNotFoundException:
                return controller.NotFound(new { error = exception.Message });
            case ThreeCardPokerTableInsufficientCreditsException insufficient:
                return controller.Conflict(new
                {
                    code = "insufficient-slot-credits",
                    error = insufficient.Message,
                    available = insufficient.Available,
                    required = insufficient.Required
                });
            case ThreeCardPokerTableConflictException:
                return controller.Conflict(new { code = "three-card-poker-table-state-conflict", error = exception.Message });
            case ThreeCardPokerTableIllegalActionException:
                return controller.BadRequest(new { code = "illegal-three-card-poker-action", error = exception.Message });
            case ArgumentException:
                return controller.BadRequest(new { error = exception.Message });
            default:
                logger.LogError(
                    exception,
                    "Three Card Poker table request failed; trace {TraceIdentifier}.",
                    controller.HttpContext.TraceIdentifier);
                return controller.Problem(
                    "Three Card Poker table mode could not complete the request.",
                    statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
