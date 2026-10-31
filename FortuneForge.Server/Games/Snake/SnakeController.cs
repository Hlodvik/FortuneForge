using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.Snake;

[ApiController]
[Route("api/games/snake")]
public sealed class SnakeController(FirestoreSnakeGameService games, AccountService accounts) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken ct) =>
        await WithAccountAsync(ct, _ => Task.FromResult<ActionResult>(Ok(games.Status())));

    [HttpPost("games")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(SnakeStartRequest? request, CancellationToken ct) =>
        await WithAccountAsync(ct, account => ExecuteAsync(() => games.StartAsync(account.UserId, request, ct)));

    [HttpGet("games/{gameId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(Guid gameId, CancellationToken ct) =>
        await WithAccountAsync(ct, account => ExecuteAsync(() => games.GetAsync(account.UserId, gameId, ct)));

    [HttpPost("games/{gameId:guid}/turn")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Turn(Guid gameId, SnakeTurnRequest request, CancellationToken ct) =>
        await WithAccountAsync(ct, account => ExecuteAsync(() => games.TurnAsync(account.UserId, gameId, request.Direction, ct)));

    [HttpPost("games/{gameId:guid}/tick")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Tick(Guid gameId, CancellationToken ct) =>
        await WithAccountAsync(ct, account => ExecuteAsync(() => games.TickAsync(account.UserId, gameId, ct)));

    [HttpPost("games/{gameId:guid}/reset")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Reset(Guid gameId, SnakeStartRequest? request, CancellationToken ct) =>
        await WithAccountAsync(ct, account => ExecuteAsync(() => games.ResetAsync(account.UserId, gameId, request, ct)));

    private async Task<ActionResult> WithAccountAsync(CancellationToken ct,
        Func<AccountSummary, Task<ActionResult>> action)
    {
        var profile = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), ct);
        return profile.Value is { } account ? await action(account)
            : Unauthorized(new SnakeErrorResponse("snake-authentication-required", "Sign in to play Snake."));
    }

    private async Task<ActionResult> ExecuteAsync(Func<Task<SnakeGameResponse>> action)
    {
        try { return Ok(await action()); }
        catch (SnakeAccessException exception) { return NotFound(new SnakeErrorResponse("snake-game-not-found", exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or SnakeRuleException)
        { return BadRequest(new SnakeErrorResponse("snake-invalid-action", exception.Message)); }
    }
}
