using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.TwentyFortyEight;

[ApiController]
[Route("api/games/2048")]
public sealed class TwentyFortyEightController(TwentyFortyEightGameService games, AccountService accounts) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken) => await WithAccount(cancellationToken, _ => Ok(games.Status()));

    [HttpPost("games")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(StartTwentyFortyEightRequest? request, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, async account => Ok(await games.StartAuditedAsync(account.UserId, request?.Seed, cancellationToken)));

    [HttpGet("games/{gameId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(Guid gameId, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.Get(account.UserId, gameId)));

    [HttpPost("games/{gameId:guid}/move")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Move(Guid gameId, TwentyFortyEightMoveRequest request, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.MoveAuditedAsync(account.UserId, gameId, request.Direction, cancellationToken)));

    [HttpPost("games/{gameId:guid}/undo")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Undo(Guid gameId, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.UndoAuditedAsync(account.UserId, gameId, cancellationToken)));

    [HttpPost("games/{gameId:guid}/continue")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Continue(Guid gameId, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.ContinueAuditedAsync(account.UserId, gameId, cancellationToken)));

    [HttpPost("games/{gameId:guid}/reset")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Reset(Guid gameId, StartTwentyFortyEightRequest? request, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.ResetAuditedAsync(account.UserId, gameId, request?.Seed, cancellationToken)));

    private async Task<ActionResult> WithAccount(CancellationToken cancellationToken, Func<AccountSummary, ActionResult> action)
    {
        var account = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken);
        return account.Value is null
            ? Unauthorized(new TwentyFortyEightErrorResponse("2048-authentication-required", "Sign in to play 2048."))
            : action(account.Value);
    }

    private async Task<ActionResult> WithAccountAsync(CancellationToken cancellationToken, Func<AccountSummary, Task<ActionResult>> action)
    {
        var account = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken);
        return account.Value is null
            ? Unauthorized(new TwentyFortyEightErrorResponse("2048-authentication-required", "Sign in to play 2048."))
            : await action(account.Value);
    }

    private async Task<ActionResult> ExecuteAsync(Func<Task<TwentyFortyEightGameResponse>> action)
    {
        try { return Ok(await action()); }
        catch (TwentyFortyEightAccessException exception) { return NotFound(new TwentyFortyEightErrorResponse("2048-game-not-found", exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return BadRequest(new TwentyFortyEightErrorResponse("2048-invalid-action", exception.Message)); }
    }

    private ActionResult Execute(Func<TwentyFortyEightGameResponse> action)
    {
        try { return Ok(action()); }
        catch (TwentyFortyEightAccessException exception) { return NotFound(new TwentyFortyEightErrorResponse("2048-game-not-found", exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return BadRequest(new TwentyFortyEightErrorResponse("2048-invalid-action", exception.Message)); }
    }
}
