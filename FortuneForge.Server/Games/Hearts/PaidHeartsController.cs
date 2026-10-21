using FortuneForge.Games.Hearts;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.Hearts;

[ApiController]
[Route("api/games/hearts/paid")]
public sealed class PaidHeartsController(
    IServiceProvider services, AccountService accounts, IConfiguration configuration) : ControllerBase
{
    private FirestorePaidHeartsStore Store => services.GetRequiredService<FirestorePaidHeartsStore>();

    [HttpGet("session")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Session(CancellationToken ct) =>
        Execute(ct, account => Store.SessionAsync(account.UserId, DateTime.UtcNow, ct));

    [HttpGet("matches/{matchId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Get(string matchId, CancellationToken ct) =>
        Execute(ct, account => Store.GetAsync(account.UserId, matchId, DateTime.UtcNow, ct));

    [HttpPost("join")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Join(JoinPaidHeartsRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        Execute(ct, account => Store.JoinAsync(account.UserId, account.PlayerName, request.EntryCredits, key ?? "", DateTime.UtcNow, ct));

    [HttpPost("matches/{matchId}/pass")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Pass(string matchId, PaidHeartsPassRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        Execute(ct, account => Store.CommandAsync(account.UserId, matchId, "pass", request.ExpectedVersion, request.Cards,
            key ?? "", DateTime.UtcNow, ct));

    [HttpPost("matches/{matchId}/card")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Card(string matchId, PaidHeartsCardRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        Execute(ct, account => Store.CommandAsync(account.UserId, matchId, "card", request.ExpectedVersion, [request.Card],
            key ?? "", DateTime.UtcNow, ct));

    [HttpPost("matches/{matchId}/advance")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> Advance(string matchId, CancellationToken ct) =>
        Execute(ct, account => Store.GetAsync(account.UserId, matchId, DateTime.UtcNow, ct));

    [HttpPost("matches/{matchId}/next-round")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> NextRound(string matchId, PaidHeartsVersionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        Execute(ct, account => Store.CommandAsync(account.UserId, matchId, "next-round", request.ExpectedVersion, null,
            key ?? "", DateTime.UtcNow, ct));

    [HttpPost("matches/{matchId}/leave")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> Leave(string matchId, PaidHeartsVersionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        Execute(ct, account => Store.CommandAsync(account.UserId, matchId, "leave", request.ExpectedVersion, null,
            key ?? "", DateTime.UtcNow, ct));

    [HttpGet("history")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public Task<ActionResult> History(CancellationToken ct) =>
        Execute(ct, account => Store.HistoryAsync(account.UserId, ct));

    private async Task<ActionResult> Execute<T>(CancellationToken ct, Func<AccountSummary, Task<T>> action)
    {
        if (!configuration.GetValue("Features:HeartsEnabled", false))
            return StatusCode(503, new HeartsErrorResponse("hearts-disabled", "Hearts is not available yet."));
        var account = (await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), ct)).Value;
        if (account is null) return Unauthorized(new HeartsErrorResponse("hearts-authentication-required", "Sign in to play Hearts."));
        try { return Ok(await action(account)); }
        catch (HeartsAccessException exception) { return NotFound(new HeartsErrorResponse("hearts-match-not-found", exception.Message)); }
        catch (PaidHeartsInsufficientFundsException exception) { return StatusCode(402, new HeartsErrorResponse("hearts-insufficient-funds", exception.Message)); }
        catch (PaidHeartsConflictException exception) { return Conflict(new HeartsErrorResponse("hearts-conflict", exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or HeartsRuleException)
        { return BadRequest(new HeartsErrorResponse("hearts-invalid-action", exception.Message)); }
    }
}
