using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Cards.VideoPoker;

[ApiController]
[Route("api/games/video-poker")]
public sealed class VideoPokerController(
    FirestoreDb database,
    AccountService accountService,
    IConfiguration configuration,
    ILogger<VideoPokerController> logger) : ControllerBase
{
    [HttpGet("status")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Status(CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        return account is null
            ? Unauthorized(new VideoPokerErrorResponse("video-poker-authentication-required", "Sign in to play Video Poker."))
            : Ok(new VideoPokerStatusResponse(
                true,
                VideoPokerMoney.MinimumCoinsWagered,
                VideoPokerMoney.MaximumCoinsWagered,
                VideoPokerMoney.ToRand(VideoPokerMoney.CoinValueCents),
                IsPractice ? VideoPokerMoney.ToRand(PracticeVideoPokerStore.StartingBalanceCents) : account.Balances.SlotsCredits,
                [1, 3, 5]));
    }

    [HttpPost("rounds")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Start(
        CreateVideoPokerRoundRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
            return Unauthorized(new VideoPokerErrorResponse("video-poker-authentication-required", "Sign in to play Video Poker."));
        try
        {
            var result = await Service().StartAsync(account.UserId, request, idempotencyKey ?? string.Empty, cancellationToken);
            return CreatedAtAction(nameof(Get), new { roundId = result.RoundId }, result);
        }
        catch (Exception exception) { return VideoPokerHttp.FromException(this, exception, logger); }
    }

    [HttpGet("rounds/{roundId}")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> Get(string roundId, CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
            return Unauthorized(new VideoPokerErrorResponse("video-poker-authentication-required", "Sign in to play Video Poker."));
        try { return Ok(await Service().GetAsync(account.UserId, roundId, cancellationToken)); }
        catch (Exception exception) { return VideoPokerHttp.FromException(this, exception, logger); }
    }

    [HttpPost("rounds/{roundId}/draw")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Draw(
        string roundId,
        DrawVideoPokerRoundRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (Disabled() is { } unavailable) return unavailable;
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
            return Unauthorized(new VideoPokerErrorResponse("video-poker-authentication-required", "Sign in to play Video Poker."));
        try
        {
            return Ok(await Service().DrawAsync(
                account.UserId, roundId, request, idempotencyKey ?? string.Empty, cancellationToken));
        }
        catch (Exception exception) { return VideoPokerHttp.FromException(this, exception, logger); }
    }

    internal static bool IsEnabled(IConfiguration source) => source.GetValue("Features:VideoPokerEnabled", false);

    private VideoPokerService Service() => new(
        IsPractice
            ? HttpContext.RequestServices.GetRequiredService<PracticeVideoPokerStore>()
            : new VideoPokerFirestoreStore(database),
        TimeProvider.System);

    private bool IsPractice => HttpContext?.Request.Headers.TryGetValue("X-FortuneForge-Practice", out var values) == true &&
        values.Any(value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

    private ActionResult? Disabled() => IsEnabled(configuration)
        ? null
        : StatusCode(StatusCodes.Status503ServiceUnavailable, new VideoPokerErrorResponse(
            "video-poker-disabled",
            "Video Poker is still being verified and cannot accept a wager yet."));

    private async Task<AccountSummary?> AuthenticatedAccountAsync(CancellationToken cancellationToken) =>
        (await accountService.GetProfileAsync(AccountSessionCookie.Read(Request), cancellationToken)).Value;
}

internal static class VideoPokerHttp
{
    public static ActionResult FromException(ControllerBase controller, Exception exception, ILogger logger) => exception switch
    {
        VideoPokerRoundNotFoundException => controller.NotFound(new VideoPokerErrorResponse("video-poker-round-not-found", exception.Message)),
        VideoPokerInsufficientCreditsException insufficient => controller.Conflict(new VideoPokerErrorResponse(
            "insufficient-slot-credits", exception.Message)),
        VideoPokerRoundConflictException => controller.Conflict(new VideoPokerErrorResponse("video-poker-round-conflict", exception.Message)),
        ArgumentException => controller.BadRequest(new VideoPokerErrorResponse("video-poker-invalid-request", exception.Message)),
        _ => Unexpected(controller, exception, logger),
    };

    private static ActionResult Unexpected(ControllerBase controller, Exception exception, ILogger logger)
    {
        logger.LogError(exception, "Video Poker request failed; trace {TraceIdentifier}.", controller.HttpContext.TraceIdentifier);
        return controller.Problem("The Video Poker service could not complete the request.", statusCode: StatusCodes.Status500InternalServerError);
    }
}
