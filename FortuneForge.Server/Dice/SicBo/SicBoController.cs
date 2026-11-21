using FortuneForge.Games.SicBo.Server;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Dice.SicBo;

[ApiController]
[Route("api/games/sic-bo")]
public sealed class SicBoController(IConfiguration configuration, AccountService accounts, IHostEnvironment environment,
    ILogger<SicBoController> logger) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Status(CancellationToken cancellationToken) => PracticeAsync(cancellationToken,
        (_, _) => Task.FromResult<ActionResult>(Ok(new SicBoStatusResponse(true,
            SicBoMoney.ToRand(SicBoMoney.MinimumStakeCents), SicBoMoney.ToRand(SicBoMoney.MaximumStakeCents),
            SicBoMoney.ToRand(SicBoMoney.StakeIncrementCents), SicBoMoney.MaximumBetsPerRound,
            SicBoMoney.ToRand(PracticeSicBoStore.StartingBalanceCents), "practice-three-dice-sic-bo"))));

    [HttpPost("rounds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Start(CreateSicBoRoundRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) => PracticeAsync(cancellationToken, async (actor, service) =>
        {
            var round = await service.StartAsync(actor, request, idempotencyKey ?? string.Empty, cancellationToken);
            return CreatedAtAction(nameof(Get), new { roundId = round.RoundId }, round);
        });

    [HttpGet("rounds/{roundId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Get(string roundId, CancellationToken cancellationToken) => PracticeAsync(cancellationToken,
        async (actor, service) => Ok(await service.GetAsync(actor, roundId, cancellationToken)));

    internal static bool IsEnabled(IConfiguration source) => source.GetValue("Features:SicBoEnabled", false);

    private async Task<ActionResult> PracticeAsync(CancellationToken token, Func<string, SicBoService, Task<ActionResult>> action)
    {
        if (!IsEnabled(configuration))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new SicBoErrorResponse(
                "sic-bo-disabled", "Sic Bo is still being verified and cannot accept a wager yet."));
        if (HttpContext?.Request.Headers["X-FortuneForge-Practice"] is not { Count: 1 } practice ||
            !string.Equals(practice[0], "true", StringComparison.OrdinalIgnoreCase) || !environment.IsDevelopment())
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new SicBoErrorResponse(
                "sic-bo-retry-required", "Retry the original request when the Sic Bo worker is available."));

        var profile = (await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), token)).Value;
        if (profile is null) return Unauthorized(new SicBoErrorResponse("sic-bo-authentication-required", "Sign in to play Sic Bo."));
        try
        {
            var store = HttpContext.RequestServices.GetRequiredService<PracticeSicBoStore>();
            return await action(profile.UserId, new SicBoService(store, TimeProvider.System));
        }
        catch (SicBoRoundNotFoundException error) { return NotFound(new SicBoErrorResponse("sic-bo-round-not-found", error.Message)); }
        catch (SicBoInsufficientCreditsException error) { return Conflict(new SicBoErrorResponse("insufficient-slot-credits", error.Message)); }
        catch (SicBoRoundConflictException error) { return Conflict(new SicBoErrorResponse("sic-bo-round-conflict", error.Message)); }
        catch (ArgumentException error) { return BadRequest(new SicBoErrorResponse("sic-bo-invalid-request", error.Message)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogError(error, "Sic Bo practice request failed; trace {TraceIdentifier}.", HttpContext.TraceIdentifier);
            return Problem("The Sic Bo practice service could not complete the request.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
