using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.Blackjack.Table;

[ApiController]
[Route("api/cards/blackjack/table")]
public sealed class BlackjackTableController(
    IServiceProvider services,
    AccountService accountService,
    IConfiguration configuration,
    ILogger<BlackjackTableController> logger) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        return account is null
            ? Unauthorized(new { error = "Sign in to view Blackjack table availability." })
            : Ok(StatusContract());
    }

    [HttpGet("session")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableReads)]
    public async Task<ActionResult> Session(CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to play Blackjack table mode." });
        if (Disabled() is { } unavailable) return unavailable;
        try { return Ok((await Service.GetSessionAsync(account.UserId, cancellationToken)).Session); }
        catch (Exception exception) { return BlackjackTableHttp.FromException(this, exception, logger); }
    }

    [HttpGet("history")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableReads)]
    public async Task<ActionResult> History(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to view Blackjack table history." });
        if (Disabled() is { } unavailable) return unavailable;
        try { return Ok(await Service.GetHistoryAsync(account.UserId, limit, cancellationToken)); }
        catch (Exception exception) { return BlackjackTableHttp.FromException(this, exception, logger); }
    }

    [HttpPost("history/{resultId}/seen")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableWrites)]
    public async Task<ActionResult> MarkHistorySeen(string resultId, CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to update Blackjack table history." });
        if (Disabled() is { } unavailable) return unavailable;
        try { return Ok(await Service.MarkHistorySeenAsync(account.UserId, resultId, cancellationToken)); }
        catch (Exception exception) { return BlackjackTableHttp.FromException(this, exception, logger); }
    }

    [HttpPost("queue")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableWrites)]
    public async Task<ActionResult> Join(
        JoinBlackjackTableQueueRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to join a Blackjack table." });
        if (Disabled() is { } unavailable) return unavailable;
        try
        {
            return Ok(ToMutation(await Service.JoinAsync(
                account.UserId, account.PlayerName, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return BlackjackTableHttp.FromException(this, exception, logger); }
    }

    [HttpDelete("queue/{ticketId}")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableWrites)]
    public async Task<ActionResult> Cancel(
        string ticketId,
        BlackjackTableVersionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to leave the Blackjack queue." });
        if (Disabled() is { } unavailable) return unavailable;
        try
        {
            return Ok(ToMutation(await Service.CancelAsync(
                account.UserId, ticketId, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return BlackjackTableHttp.FromException(this, exception, logger); }
    }

    [HttpPost("tables/{tableId}/wagers")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableWrites)]
    public async Task<ActionResult> Wager(
        string tableId,
        BlackjackTableWagerRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to wager at a Blackjack table." });
        if (Disabled() is { } unavailable) return unavailable;
        try
        {
            return Ok(ToMutation(await Service.WagerAsync(
                account.UserId, tableId, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return BlackjackTableHttp.FromException(this, exception, logger); }
    }

    [HttpPost("tables/{tableId}/actions")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableWrites)]
    public async Task<ActionResult> Action(
        string tableId,
        BlackjackTableActionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to act at a Blackjack table." });
        if (Disabled() is { } unavailable) return unavailable;
        try
        {
            return Ok(ToMutation(await Service.ActionAsync(
                account.UserId, tableId, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return BlackjackTableHttp.FromException(this, exception, logger); }
    }

    [HttpPost("tables/{tableId}/leave")]
    [EnableRateLimiting(RateLimitPolicies.BlackjackTableWrites)]
    public async Task<ActionResult> Leave(
        string tableId,
        BlackjackTableVersionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new { error = "Sign in to leave a Blackjack table." });
        if (Disabled() is { } unavailable) return unavailable;
        try
        {
            return Ok(ToMutation(await Service.LeaveAsync(
                account.UserId, tableId, request, idempotencyKey ?? string.Empty, cancellationToken)));
        }
        catch (Exception exception) { return BlackjackTableHttp.FromException(this, exception, logger); }
    }

    internal static bool IsEnabled(IConfiguration source) =>
        source.GetValue("Features:BlackjackTableEnabled", false);

    internal static BlackjackTableStatusResponse StatusContract() => new(
        true,
        BlackjackMoney.ToRand(BlackjackMoney.MinimumWagerCents),
        BlackjackMoney.ToRand(BlackjackMoney.MaximumWagerCents),
        BlackjackMoney.ToRand(BlackjackMoney.WagerIncrementCents),
        BlackjackTableEngine.MinimumStartOccupancy,
        BlackjackTableEngine.Capacity,
        checked((int)BlackjackTableEngine.HumanGrace.TotalSeconds),
        checked((int)BlackjackTableEngine.ActionDuration.TotalSeconds),
        "Dealer stands on all 17s",
        "3:2",
        true,
        true,
        true,
        true);

    private ActionResult? Disabled() => IsEnabled(configuration) ? null : StatusCode(
        StatusCodes.Status503ServiceUnavailable,
        new
        {
            code = "blackjack-table-disabled",
            error = "Blackjack table mode is disabled while its multiplayer accounting controls are being verified."
        });

    private async Task<AccountSummary?> AccountAsync(CancellationToken cancellationToken) =>
        (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;

    private BlackjackTableService Service => services.GetRequiredService<BlackjackTableService>();

    private static BlackjackTableMutationResponse ToMutation(BlackjackTableStoreResult value) =>
        new(value.Session, BlackjackMoney.ToRand(value.BalanceCents));
}

internal static class BlackjackTableHttp
{
    public static ActionResult FromException(ControllerBase controller, Exception exception, ILogger logger)
    {
        switch (exception)
        {
            case BlackjackTableNotFoundException:
                return controller.NotFound(new { error = exception.Message });
            case BlackjackTableInsufficientCreditsException insufficient:
                return controller.Conflict(new
                {
                    code = "insufficient-slot-credits",
                    error = insufficient.Message,
                    available = insufficient.Available,
                    required = insufficient.Required
                });
            case BlackjackTableConflictException:
                return controller.Conflict(new { code = "blackjack-table-state-conflict", error = exception.Message });
            case BlackjackTableIllegalActionException:
                return controller.BadRequest(new { code = "illegal-blackjack-action", error = exception.Message });
            case ArgumentException:
                return controller.BadRequest(new { error = exception.Message });
            default:
                logger.LogError(
                    exception,
                    "Blackjack table request failed; trace {TraceIdentifier}.",
                    controller.HttpContext.TraceIdentifier);
                return controller.Problem(
                    "Blackjack table mode could not complete the request.",
                    statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
