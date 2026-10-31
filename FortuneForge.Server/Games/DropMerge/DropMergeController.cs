using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Games.DropMerge;

[ApiController]
[Route("api/games/drop-merge")]
public sealed class DropMergeController(DropMergeGameService games, AccountService accounts) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken) => await WithAccount(cancellationToken, _ => Ok(games.Status()));

    [HttpPost("games")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(StartDropMergeRequest? request, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, async account => Ok(await games.StartAuditedAsync(account.UserId, request?.Seed, cancellationToken)));

    [HttpGet("games/{gameId:guid}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(Guid gameId, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.Get(account.UserId, gameId)));

    [HttpPost("games/{gameId:guid}/drop")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Drop(Guid gameId, DropMergeDropRequest request, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.DropAuditedAsync(account.UserId, gameId, request.Column, cancellationToken)));

    [HttpPost("games/{gameId:guid}/undo")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Undo(Guid gameId, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.UndoAuditedAsync(account.UserId, gameId, cancellationToken)));

    [HttpPost("games/{gameId:guid}/reset")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Reset(Guid gameId, StartDropMergeRequest? request, CancellationToken cancellationToken) =>
        await WithAccountAsync(cancellationToken, account => ExecuteAsync(() => games.ResetAuditedAsync(account.UserId, gameId, request?.Seed, cancellationToken)));

    private async Task<ActionResult> WithAccount(CancellationToken cancellationToken, Func<AccountSummary, ActionResult> action)
    {
        var account = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken);
        return account.Value is null
            ? Unauthorized(new DropMergeErrorResponse("drop-merge-authentication-required", "Sign in to play Drop Merge."))
            : action(account.Value);
    }

    private async Task<ActionResult> WithAccountAsync(CancellationToken cancellationToken, Func<AccountSummary, Task<ActionResult>> action)
    {
        var account = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken);
        return account.Value is null
            ? Unauthorized(new DropMergeErrorResponse("drop-merge-authentication-required", "Sign in to play Drop Merge."))
            : await action(account.Value);
    }

    private async Task<ActionResult> ExecuteAsync(Func<Task<DropMergeGameResponse>> action)
    {
        try { return Ok(await action()); }
        catch (DropMergeAccessException exception) { return NotFound(new DropMergeErrorResponse("drop-merge-game-not-found", exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return BadRequest(new DropMergeErrorResponse("drop-merge-invalid-action", exception.Message)); }
    }

    private ActionResult Execute(Func<DropMergeGameResponse> action)
    {
        try { return Ok(action()); }
        catch (DropMergeAccessException exception) { return NotFound(new DropMergeErrorResponse("drop-merge-game-not-found", exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return BadRequest(new DropMergeErrorResponse("drop-merge-invalid-action", exception.Message)); }
    }
}
