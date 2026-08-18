using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.Blackjack;

[ApiController]
[Route("api/cards/blackjack")]
public sealed class BlackjackController(
    FirestoreDb database,
    AccountService accountService,
    IConfiguration configuration,
    ILogger<BlackjackController> logger) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        return account is null
            ? Unauthorized(new { error = "Sign in to play Fortune Blackjack." })
            : Ok(BlackjackHttp.Status());
    }

    [HttpPost("games")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(
        BlackjackStartRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized(new { error = "Sign in to play Fortune Blackjack." });
        }

        try
        {
            var result = await Service().StartAsync(
                account.UserId,
                request,
                idempotencyKey ?? string.Empty,
                cancellationToken);
            return CreatedAtAction(nameof(Get), new { gameId = result.GameId }, result);
        }
        catch (Exception exception)
        {
            return BlackjackHttp.FromException(this, exception, logger);
        }
    }

    [HttpGet("games/{gameId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(string gameId, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized(new { error = "Sign in to play Fortune Blackjack." });
        }

        try
        {
            return Ok(await Service().GetAsync(account.UserId, gameId, cancellationToken));
        }
        catch (Exception exception)
        {
            return BlackjackHttp.FromException(this, exception, logger);
        }
    }

    [HttpPost("games/{gameId}/actions")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Act(
        string gameId,
        BlackjackActionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
        {
            return Unauthorized(new { error = "Sign in to play Fortune Blackjack." });
        }

        try
        {
            return Ok(await Service().ActAsync(
                account.UserId,
                gameId,
                request,
                idempotencyKey ?? string.Empty,
                cancellationToken));
        }
        catch (Exception exception)
        {
            return BlackjackHttp.FromException(this, exception, logger);
        }
    }

    private BlackjackService Service() => new(new FirestoreBlackjackStore(database));

    private ActionResult? Disabled() => IsEnabled(configuration)
        ? null
        : StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
            code = "blackjack-disabled",
            error = "Fortune Blackjack is still being verified and cannot accept a wager yet."
        });

    internal static bool IsEnabled(IConfiguration source) =>
        source.GetValue("Features:BlackjackEnabled", false);

    private async Task<Accounts.Models.AccountSummary?> AuthenticatedAccountAsync(
        CancellationToken cancellationToken)
    {
        var result = await accountService.GetProfileAsync(
            AccountSessionCookie.Read(Request),
            cancellationToken);
        return result.Value;
    }
}

[ApiController]
[Route("api/cards/blackjack/demo")]
public sealed class DemoBlackjackController(
    ILogger<DemoBlackjackController> logger) : ControllerBase
{
    private static readonly DemoBlackjackService Service = new();

    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public ActionResult Status() => Ok(BlackjackHttp.Status());

    [HttpPost("games")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public ActionResult Start(
        BlackjackStartRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromHeader(Name = "X-Demo-Session-Id")] string? demoSessionId)
    {
        try
        {
            var result = Service.Start(
                demoSessionId ?? string.Empty,
                request,
                idempotencyKey ?? string.Empty);
            return CreatedAtAction(nameof(Get), new { gameId = result.GameId }, result);
        }
        catch (Exception exception)
        {
            return BlackjackHttp.FromException(this, exception, logger);
        }
    }

    [HttpGet("games/{gameId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public ActionResult Get(
        string gameId,
        [FromHeader(Name = "X-Demo-Session-Id")] string? demoSessionId)
    {
        try
        {
            return Ok(Service.Get(demoSessionId ?? string.Empty, gameId));
        }
        catch (Exception exception)
        {
            return BlackjackHttp.FromException(this, exception, logger);
        }
    }

    [HttpPost("games/{gameId}/actions")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public ActionResult Act(
        string gameId,
        BlackjackActionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromHeader(Name = "X-Demo-Session-Id")] string? demoSessionId)
    {
        try
        {
            return Ok(Service.Act(
                demoSessionId ?? string.Empty,
                gameId,
                request,
                idempotencyKey ?? string.Empty));
        }
        catch (Exception exception)
        {
            return BlackjackHttp.FromException(this, exception, logger);
        }
    }
}

internal static class BlackjackHttp
{
    public static BlackjackStatusResponse Status() => new(
        true,
        BlackjackMoney.ToRand(BlackjackMoney.MinimumWagerCents),
        BlackjackMoney.ToRand(BlackjackMoney.MaximumWagerCents),
        BlackjackMoney.ToRand(BlackjackMoney.WagerIncrementCents),
        "Dealer stands on all 17s",
        "3:2",
        true,
        false,
        false);

    public static ActionResult FromException(
        ControllerBase controller,
        Exception exception,
        ILogger logger)
    {
        switch (exception)
        {
            case BlackjackNotFoundException:
                return controller.NotFound(new { error = exception.Message });
            case BlackjackInsufficientCreditsException insufficient:
                return controller.Conflict(new
                {
                    code = "insufficient-slot-credits",
                    error = insufficient.Message,
                    available = insufficient.Available,
                    required = insufficient.Required
                });
            case BlackjackConflictException:
                return controller.Conflict(new
                {
                    code = "blackjack-state-conflict",
                    error = exception.Message
                });
            case ArgumentException:
                return controller.BadRequest(new { error = exception.Message });
            default:
                logger.LogError(
                    exception,
                    "Blackjack request failed; trace {TraceIdentifier}.",
                    controller.HttpContext.TraceIdentifier);
                return controller.Problem(
                    "The Blackjack service could not complete the request.",
                    statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
