using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.GameWorkers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.CasinoWar;

[ApiController]
[Route("api/games/casino-war")]
public sealed class CasinoWarController(AccountService accountService,
    IConfiguration configuration, ILogger<CasinoWarController> logger) : ControllerBase
{
    [HttpGet("status")][EnableRateLimiting(RateLimitPolicies.SlotReads)] public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable; var account = await AccountAsync(cancellationToken);
        return account is null ? Unauthorized(new CasinoWarErrorResponse("casino-war-authentication-required", "Sign in to play Casino War.")) : Ok(new CasinoWarStatusResponse(true, CasinoWarMoney.ToRand(CasinoWarMoney.MinimumPrimaryStakeCents), CasinoWarMoney.ToRand(CasinoWarMoney.MaximumPrimaryStakeCents), CasinoWarMoney.ToRand(CasinoWarMoney.StakeIncrementCents), CasinoWarMoney.ToRand(CasinoWarMoney.MaximumTieStakeCents), IsPractice ? CasinoWarMoney.ToRand(PracticeCasinoWarStore.StartingBalanceCents) : account.Balances.SlotsCredits, IsPractice ? "practice-six-deck-casino-war" : "six-deck-casino-war"));
    }
    [HttpPost("rounds")][EnableRateLimiting(RateLimitPolicies.SlotSpins)] public async Task<ActionResult> Start(CreateCasinoWarRoundRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable; var account = await AccountAsync(cancellationToken); if (account is null) return Unauthorized(new CasinoWarErrorResponse("casino-war-authentication-required", "Sign in to play Casino War."));
        try { var result = await Service().StartAsync(account.UserId, request, idempotencyKey ?? string.Empty, cancellationToken); return CreatedAtAction(nameof(Get), new { roundId = result.RoundId }, result); } catch (Exception exception) { return CasinoWarHttp.FromException(this, exception, logger); }
    }
    [HttpGet("rounds/{roundId}")][EnableRateLimiting(RateLimitPolicies.SlotReads)] public async Task<ActionResult> Get(string roundId, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable; var account = await AccountAsync(cancellationToken); if (account is null) return Unauthorized(new CasinoWarErrorResponse("casino-war-authentication-required", "Sign in to play Casino War."));
        try { return Ok(await Service().GetAsync(account.UserId, roundId, cancellationToken)); } catch (Exception exception) { return CasinoWarHttp.FromException(this, exception, logger); }
    }
    [HttpPost("rounds/{roundId}/decision")][EnableRateLimiting(RateLimitPolicies.SlotSpins)] public async Task<ActionResult> Decide(string roundId, DecideCasinoWarRoundRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable; var account = await AccountAsync(cancellationToken); if (account is null) return Unauthorized(new CasinoWarErrorResponse("casino-war-authentication-required", "Sign in to play Casino War."));
        try { return Ok(await Service().DecideAsync(account.UserId, roundId, request, idempotencyKey ?? string.Empty, cancellationToken)); } catch (Exception exception) { return CasinoWarHttp.FromException(this, exception, logger); }
    }
    internal static bool IsEnabled(IConfiguration source) => source.GetValue("Features:CasinoWarEnabled", false);
    [HttpGet("rounds/pending")][EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Pending(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        return account is null ? Unauthorized(new CasinoWarErrorResponse("casino-war-authentication-required", "Sign in to play Casino War."))
            : Ok(HttpContext.RequestServices.GetRequiredService<PracticeCasinoWarStore>().Recover(account.UserId));
    }
    [HttpGet("rounds/history")][EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> History([FromQuery] int? limit, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AccountAsync(cancellationToken);
        if (account is null) return Unauthorized(new CasinoWarErrorResponse("casino-war-authentication-required", "Sign in to play Casino War."));
        try { return Ok(HttpContext.RequestServices.GetRequiredService<PracticeCasinoWarStore>().History(account.UserId, limit ?? 20)); }
        catch (Exception exception) { return CasinoWarHttp.FromException(this, exception, logger); }
    }
    private CasinoWarService Service() => IsPractice
        ? new(HttpContext.RequestServices.GetRequiredService<PracticeCasinoWarStore>(), TimeProvider.System)
        : throw new InvalidOperationException("Paid Casino War actions require the game backend.");
    private bool IsPractice => HttpContext?.Request.Headers.TryGetValue("X-FortuneForge-Practice", out var values) == true && values.Any(value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
    private ActionResult? Disabled() => !IsEnabled(configuration)
        ? StatusCode(StatusCodes.Status503ServiceUnavailable, new CasinoWarErrorResponse("casino-war-disabled", "Casino War is still being verified and cannot accept a wager yet."))
        : IsPractice ? null : WorkerRequired();
    private ActionResult WorkerRequired() => StatusCode(StatusCodes.Status503ServiceUnavailable,
        new CasinoWarErrorResponse("casino-war-worker-required", "Paid Casino War rounds require the game backend. Retry the original request when it is available."));
    private async Task<AccountSummary?> AccountAsync(CancellationToken cancellationToken) => (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
}

internal static class CasinoWarHttp
{
    public static ActionResult FromException(ControllerBase controller, Exception exception, ILogger logger)
    {
        if (CasinoWarErrors.From(exception) is { } failure)
            return controller.StatusCode(failure.StatusCode, failure.Error);
        return exception is GameWorkerUnavailableException
            ? controller.StatusCode(StatusCodes.Status503ServiceUnavailable, new CasinoWarErrorResponse("casino-war-unavailable", exception.Message))
            : Unexpected(controller, exception, logger);
    }
    private static ActionResult Unexpected(ControllerBase controller, Exception exception, ILogger logger) { logger.LogError(exception, "Casino War request failed; trace {TraceIdentifier}.", controller.HttpContext.TraceIdentifier); return controller.Problem("The Casino War service could not complete the request.", statusCode: StatusCodes.Status500InternalServerError); }
}
