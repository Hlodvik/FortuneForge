using FortuneForge.Games.DropMerge;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.GameWorkers;
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
    public ActionResult Start(StartDropMergeRequest? request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status503ServiceUnavailable, DropMergeErrors.WorkerUnavailable());

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

    private Task<ActionResult> WithAccount(CancellationToken cancellationToken, Func<AccountSummary, ActionResult> action) =>
        WithAccountAsync(cancellationToken, account => Task.FromResult(action(account)));

    private async Task<ActionResult> WithAccountAsync(CancellationToken cancellationToken, Func<AccountSummary, Task<ActionResult>> action)
    {
        var account = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken);
        if (account.Value is null)
            return Unauthorized(DropMergeErrors.AuthenticationRequired());
        if (!GameAccountBinding.Matches(Request, account.Value.UserId))
            return StatusCode(StatusCodes.Status403Forbidden, GameAccountBinding.SessionChanged);
        try { return await action(account.Value); }
        catch (GameWorkerUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                DropMergeErrors.WorkerUnavailable());
        }
    }

    private async Task<ActionResult> ExecuteAsync(Func<Task<DropMergeGameResponse>> action)
    {
        try { return Ok(await action()); }
        catch (DropMergeAccessException exception) { return NotFound(DropMergeErrors.GameNotFound(exception)); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return BadRequest(DropMergeErrors.InvalidAction(exception.Message)); }
    }

    private ActionResult Execute(Func<DropMergeGameResponse> action)
    {
        try { return Ok(action()); }
        catch (DropMergeAccessException exception) { return NotFound(DropMergeErrors.GameNotFound(exception)); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return BadRequest(DropMergeErrors.InvalidAction(exception.Message)); }
    }
}
