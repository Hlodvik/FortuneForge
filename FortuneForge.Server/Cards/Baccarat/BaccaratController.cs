using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.Baccarat;

[ApiController]
[Route("api/games/baccarat")]
public sealed class BaccaratController(
    FirestoreDb database,
    AccountService accountService,
    IConfiguration configuration,
    ILogger<BaccaratController> logger) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        return account is null
            ? Unauthorized(new BaccaratErrorResponse("baccarat-authentication-required", "Sign in to play Baccarat."))
            : Ok(new BaccaratStatusResponse(
                true,
                BaccaratMoney.ToRand(BaccaratMoney.MinimumStakeCents),
                BaccaratMoney.ToRand(BaccaratMoney.MaximumStakeCents),
                BaccaratMoney.ToRand(BaccaratMoney.StakeIncrementCents),
                IsPractice ? BaccaratMoney.ToRand(PracticeBaccaratStore.StartingBalanceCents) : account.Balances.SlotsCredits,
                IsPractice ? "practice-eight-deck-punto-banco" : "eight-deck-punto-banco"));
    }

    [HttpPost("rounds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(
        CreateBaccaratRoundRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
            return Unauthorized(new BaccaratErrorResponse("baccarat-authentication-required", "Sign in to play Baccarat."));
        try
        {
            var result = await Service().StartAsync(account.UserId, request, idempotencyKey ?? string.Empty, cancellationToken);
            return CreatedAtAction(nameof(Get), new { roundId = result.RoundId }, result);
        }
        catch (Exception exception) { return BaccaratHttp.FromException(this, exception, logger); }
    }

    [HttpGet("rounds/{roundId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(string roundId, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
            return Unauthorized(new BaccaratErrorResponse("baccarat-authentication-required", "Sign in to play Baccarat."));
        try { return Ok(await Service().GetAsync(account.UserId, roundId, cancellationToken)); }
        catch (Exception exception) { return BaccaratHttp.FromException(this, exception, logger); }
    }

    internal static bool IsEnabled(IConfiguration source) => source.GetValue("Features:BaccaratEnabled", false);

    private BaccaratService Service() => new(
        IsPractice
            ? HttpContext.RequestServices.GetRequiredService<PracticeBaccaratStore>()
            : new BaccaratFirestoreStore(database),
        TimeProvider.System);

    private bool IsPractice => HttpContext?.Request.Headers.TryGetValue("X-FortuneForge-Practice", out var values) == true &&
        values.Any(value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

    private ActionResult? Disabled() => IsEnabled(configuration)
        ? null
        : StatusCode(StatusCodes.Status503ServiceUnavailable, new BaccaratErrorResponse(
            "baccarat-disabled",
            "Baccarat is still being verified and cannot accept a wager yet."));

    private async Task<AccountSummary?> AuthenticatedAccountAsync(CancellationToken cancellationToken) =>
        (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
}

internal static class BaccaratHttp
{
    public static ActionResult FromException(ControllerBase controller, Exception exception, ILogger logger) => exception switch
    {
        BaccaratRoundNotFoundException => controller.NotFound(new BaccaratErrorResponse("baccarat-round-not-found", exception.Message)),
        BaccaratInsufficientCreditsException => controller.Conflict(new BaccaratErrorResponse("insufficient-slot-credits", exception.Message)),
        BaccaratRoundConflictException => controller.Conflict(new BaccaratErrorResponse("baccarat-round-conflict", exception.Message)),
        ArgumentException => controller.BadRequest(new BaccaratErrorResponse("baccarat-invalid-request", exception.Message)),
        _ => Unexpected(controller, exception, logger),
    };

    private static ActionResult Unexpected(ControllerBase controller, Exception exception, ILogger logger)
    {
        logger.LogError(exception, "Baccarat request failed; trace {TraceIdentifier}.", controller.HttpContext.TraceIdentifier);
        return controller.Problem("The Baccarat service could not complete the request.", statusCode: StatusCodes.Status500InternalServerError);
    }
}
