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
        await WithAccount(cancellationToken, account => Ok(games.Start(account.UserId, request?.Seed)));

    [HttpPost("games/{gameId:guid}/move")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Move(Guid gameId, TwentyFortyEightMoveRequest request, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.Move(account.UserId, gameId, request.Direction)));

    [HttpPost("games/{gameId:guid}/undo")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Undo(Guid gameId, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.Undo(account.UserId, gameId)));

    [HttpPost("games/{gameId:guid}/continue")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Continue(Guid gameId, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.Continue(account.UserId, gameId)));

    [HttpPost("games/{gameId:guid}/reset")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Reset(Guid gameId, StartTwentyFortyEightRequest? request, CancellationToken cancellationToken) =>
        await WithAccount(cancellationToken, account => Execute(() => games.Reset(account.UserId, gameId, request?.Seed)));

    private async Task<ActionResult> WithAccount(CancellationToken cancellationToken, Func<AccountSummary, ActionResult> action)
    {
        var account = await accounts.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken);
        return account.Value is null
            ? Unauthorized(new TwentyFortyEightErrorResponse("2048-authentication-required", "Sign in to play 2048."))
            : action(account.Value);
    }

    private ActionResult Execute(Func<TwentyFortyEightGameResponse> action)
    {
        try { return Ok(action()); }
        catch (TwentyFortyEightAccessException exception) { return NotFound(new TwentyFortyEightErrorResponse("2048-game-not-found", exception.Message)); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        { return BadRequest(new TwentyFortyEightErrorResponse("2048-invalid-action", exception.Message)); }
    }
}
