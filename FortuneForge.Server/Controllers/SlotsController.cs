using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Games.JadeDragon;
using FortuneForge.Server.GameWorkers;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Spins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Controllers;

public sealed record SpinRequest(
    string GameId,
    long WagerPoints,
    bool UseFreeSpin,
    bool UseSpecialBoost,
    string? JadeDragonEngineVersion = null);

public sealed record NeonAdminTriggerRequest(string Symbol, long WagerPoints);
public sealed record NeonCarRespinRequest(Guid SpinId, int? ReelIndex);
public sealed record NeonCarRespinDismissRequest(Guid SpinId);
public sealed record NeonNitroChoiceRequest(Guid SpinId, int? ReelIndex);

[ApiController]
[Route("api/slots")]
public sealed class SlotsController(
    SpinService spinService,
    AccountService accountService,
    ILogger<SlotsController> logger,
    GameWorkerClient? workers = null) : ControllerBase
{
    private static readonly TimeSpan SpinCooldown = TimeSpan.FromMilliseconds(500);

    [HttpPost("admin/neon-trigger")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> AdminNeonTrigger(
        NeonAdminTriggerRequest request,
        CancellationToken cancellationToken)
    {
        var account = await accountService.GetAccessContextAsync(SessionToken(), cancellationToken);
        if (account is null)
            return Unauthorized(new { error = "Sign in to play Fortune Slots." });
        if (!account.IsAdmin)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "Only administrators can start a Neon preview round." });

        if (request.Symbol is not ("car" or "disco-ball"))
            return BadRequest(new { error = "Unknown Neon Special Plays symbol." });

        try
        {
            var gameId = SlotSpecialRoundProfiles.NeonNightsGameId;
            spinService.ValidateRequest(gameId, request.WagerPoints, false);
            NeonArcadeSlotDefinitions.ValidateCarryWager(request.WagerPoints);
            var pointValueInCents = spinService.GetPointValueInCents(gameId);
            var granted = await accountService.GrantNeonAdminSpecialPlaysAsync(
                account.UserId,
                request.WagerPoints,
                pointValueInCents,
                request.Symbol == "car",
                request.Symbol == "disco-ball",
                cancellationToken);
            if (!granted)
                return Conflict(new { error = "Finish the current Neon spin or Special Plays round first." });
            logger.LogInformation("Administrator {UserId} started Neon {Symbol} Special Plays.",
                account.UserId, request.Symbol);
            return Ok(await accountService.GetSlotStateAsync(
                account.UserId, gameId, pointValueInCents, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("state")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> State(
        [FromQuery] string gameId,
        CancellationToken cancellationToken)
    {
        try
        {
            spinService.ValidateGame(gameId);
            var pointValueInCents = spinService.GetPointValueInCents(gameId);
            var accountResult = await accountService.GetProfileAsync(SessionToken(), cancellationToken);
            if (accountResult.Value is null)
            {
                return Unauthorized(new { error = "Sign in to play Fortune Slots." });
            }

            return Ok(await accountService.GetSlotStateAsync(
                accountResult.Value.UserId,
                gameId,
                pointValueInCents,
                cancellationToken));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(
                "Rejected malformed slot state request for game {GameId}; trace {TraceIdentifier}.",
                gameId,
                HttpContext.TraceIdentifier);
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpPost("neon/car-respin")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> RespinNeonCarReel(NeonCarRespinRequest request, CancellationToken cancellationToken)
    {
        var account = await accountService.GetAccessContextAsync(SessionToken(), cancellationToken);
        if (account is null)
            return Unauthorized(new { error = "Sign in to play Fortune Slots." });
        if (request.SpinId == Guid.Empty || request.ReelIndex is not { } reelIndex || reelIndex is < 0 or >= 5)
            return BadRequest(new { error = "Choose an available reel from the latest Car Play." });

        try
        {
            var result = await accountService.RespinNeonCarReelAsync(account.UserId, request.SpinId,
                reelIndex, cancellationToken);
            return result.PendingNeonNitroChoice is { } pending
                ? Accepted(new { pendingNeonNitroChoice = pending })
                : Ok(result);
        }
        catch (NeonCarRespinConflictException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpPost("neon/nitro-choice")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> ChooseNeonNitroReel(NeonNitroChoiceRequest request, CancellationToken cancellationToken) =>
        ResolveNitroChoice(request, fromRespin: false, cancellationToken);

    [HttpPost("neon/car-respin/nitro-choice")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> ChooseNeonRespinNitroReel(NeonNitroChoiceRequest request, CancellationToken cancellationToken) =>
        ResolveNitroChoice(request, fromRespin: true, cancellationToken);

    private async Task<ActionResult> ResolveNitroChoice(
        NeonNitroChoiceRequest request, bool fromRespin, CancellationToken cancellationToken)
    {
        var account = await accountService.GetAccessContextAsync(SessionToken(), cancellationToken);
        if (account is null)
            return Unauthorized(new { error = "Sign in to play Fortune Slots." });
        if (request.SpinId == Guid.Empty || request.ReelIndex is not { } reelIndex || reelIndex is < 0 or >= 5)
            return BadRequest(new { error = "Choose one of the five reels for Nitro Wild." });

        try
        {
            if (fromRespin)
                return Ok(await accountService.FinalizeNeonCarRespinNitroChoiceAsync(
                    account.UserId, request.SpinId, reelIndex, cancellationToken));

            var finalized = await accountService.FinalizeNeonNitroChoiceAsync(
                account.UserId, request.SpinId, reelIndex, cancellationToken);
            return Ok(ApplySettlement(finalized.Result, finalized.Settlement,
                finalized.Result.IsFreeSpin, finalized.Result.ChargedWagerCents));
        }
        catch (NeonNitroChoiceConflictException exception)
        {
            return Conflict(new { code = "nitro-choice-conflict", error = exception.Message });
        }
        catch (NeonCarRespinConflictException exception)
        {
            return Conflict(new { code = "nitro-choice-conflict", error = exception.Message });
        }
    }

    [HttpPost("neon/car-respin/dismiss")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> DismissNeonCarRespin(NeonCarRespinDismissRequest request, CancellationToken cancellationToken)
    {
        var account = await accountService.GetAccessContextAsync(SessionToken(), cancellationToken);
        if (account is null)
            return Unauthorized(new { error = "Sign in to play Fortune Slots." });
        if (request.SpinId == Guid.Empty)
            return BadRequest(new { error = "Choose the completed Car Play." });

        await accountService.DismissNeonCarRespinAsync(account.UserId, request.SpinId, cancellationToken);
        return NoContent();
    }

    [HttpPost("spins")]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Spin(
        SpinRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var accountResult = await accountService.GetProfileAsync(
                SessionToken(),
                cancellationToken);
            if (accountResult.Value is null)
            {
                return Unauthorized(new
                {
                    error = "Sign in to play Fortune Slots."
                });
            }

            var account = accountResult.Value;
            if (request.GameId == JadeDragonEngine.GameId &&
                request.JadeDragonEngineVersion != JadeDragonCascadeRules.Version)
                return BadRequest(new { error = "Reload Jade Dragon to use the current game rules." });
            FortuneForge.Server.Slots.Games.JadeDragon.JadeDragonWagerPolicy.Validate(
                request.GameId, request.WagerPoints, request.UseFreeSpin);
            spinService.ValidateRequest(request.GameId, request.WagerPoints, request.UseFreeSpin);
            var pointValueInCents = spinService.GetPointValueInCents(request.GameId);
            var specialBoostCost = request.UseSpecialBoost
                ? spinService.GetSpecialBoostCost(request.GameId)
                : 0;
            var admission = await accountService.BeginSlotSpinAsync(
                account.UserId,
                request.GameId,
                request.WagerPoints,
                pointValueInCents,
                request.UseFreeSpin,
                request.UseSpecialBoost,
                specialBoostCost,
                DateTime.UtcNow,
                SpinCooldown,
                cancellationToken,
                workers?.IsConfigured(request.GameId) == true
                    ? (pending, token) => PaidSlotWorker.ResolveAsync(workers, request.GameId, account.UserId, pending, token)
                    : request.GameId == SlotSpecialRoundProfiles.NeonNightsGameId
                        ? (pending, _) => Task.FromResult(spinService.SpinAdmitted(request.GameId, pending, account.UserId))
                        : null);
            if (admission.PendingNeonNitroChoice is { } nitroChoice)
                return Accepted(new { pendingNeonNitroChoice = nitroChoice });
            if (admission.CooldownRemaining is { } remaining)
            {
                var retryAfterMilliseconds = Math.Max(1, (int)Math.Ceiling(remaining.TotalMilliseconds));
                Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    error = "Wait for the previous spin to finish.",
                    retryAfterMilliseconds
                });
            }

            var result = admission.ResolvedOutcome ?? spinService.SpinAdmitted(request.GameId, admission, account.UserId);
            var settlement = await accountService.RecordSlotSpinAsync(
                account.UserId,
                result,
                admission,
                // Admission has consumed any free/boost credit. Complete settlement
                // even if the player navigates away or the HTTP request disconnects.
                CancellationToken.None);
            return Ok(ApplySettlement(result, settlement, admission.IsFreeSpin, admission.ChargedWagerCents));
        }
        catch (GameWorkerUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (InsufficientSlotCreditsException exception)
        {
            return Conflict(new
            {
                code = "insufficient-slot-credits",
                error = exception.Message,
                available = exception.Available,
                required = exception.Required
            });
        }
        catch (NoFreeSpinsException exception)
        {
            return Conflict(new
            {
                code = "free-spins-unavailable",
                error = exception.Message,
                freeSpinsRemaining = 0
            });
        }
        catch (SlotFreeSpinsRequiredException exception)
        {
            return Conflict(new { code = "free-spins-required", error = exception.Message });
        }
        catch (NeonNitroChoiceConflictException exception)
        {
            return Conflict(new { code = "nitro-choice-required", error = exception.Message });
        }
        catch (InsufficientSpecialPointsException exception)
        {
            return Conflict(new
            {
                code = "insufficient-special-points",
                error = exception.Message,
                available = exception.Available,
                required = exception.Required
            });
        }
        catch (ArgumentOutOfRangeException exception)
        {
            logger.LogWarning(
                "Rejected slot wager {WagerPoints} for game {GameId}; trace {TraceIdentifier}.",
                request.WagerPoints,
                request.GameId,
                HttpContext.TraceIdentifier);
            return BadRequest(new { error = exception.Message });
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(
                "Rejected malformed slot request for game {GameId}; trace {TraceIdentifier}.",
                request.GameId,
                HttpContext.TraceIdentifier);
            return BadRequest(new { error = exception.Message });
        }
    }

    private static SpinResult ApplySettlement(
        SpinResult result, SlotSpinSettlement settlement, bool isFreeSpin, long chargedWagerCents) =>
        result with
        {
            SlotsCreditsBalance = settlement.SlotsCreditsBalance,
            Payout = settlement.Payout,
            FreeSpinsAwarded = result.GameId == SlotSpecialRoundProfiles.PiratesFortuneGameId
                ? checked(result.FreeSpinsAwarded + settlement.SealFreeSpinsAwarded)
                : result.FreeSpinsAwarded,
            CollectionFreeSpinsAwarded = result.GameId == SlotSpecialRoundProfiles.PiratesFortuneGameId
                ? settlement.SealFreeSpinsAwarded : 0,
            IsFreeSpin = isFreeSpin,
            ChargedWagerCents = result.GameId == SlotSpecialRoundProfiles.NeonNightsGameId ||
                result.JadeDragonCascade is not null ? chargedWagerCents : 0,
            FreeSpinsRemaining = settlement.FreeSpinsRemaining,
            NeonDiscoSpinsRemaining = settlement.NeonDiscoSpinsRemaining,
            NeonDiscoMultiplier = settlement.NeonDiscoMultiplier,
            NeonDiscoRoundWinPoints = settlement.NeonDiscoRoundWinPoints,
            NeonCarRespinOffer = settlement.NeonCarRespinOffer,
            NeonCarRoundWinPoints = settlement.NeonCarRoundWinPoints,
            FreeSpinWagerPoints = settlement.FreeSpinWagerPoints,
            SpecialPointsBalance = settlement.SpecialPointsBalance,
            EnergyBalance = settlement.EnergyBalance,
            EnergyMultiplierApplied = settlement.EnergyMultiplierApplied,
            CrucibleWageredCents = settlement.CrucibleWageredCents,
            CruciblePourValueCents = settlement.CruciblePourValueCents,
            CruciblePoured = settlement.CruciblePoured,
            CruciblePayoutCents = settlement.CruciblePayoutCents,
            PayoutMultiplier = settlement.PayoutMultiplier,
            SealCollections = settlement.SealCollections,
            FreeSpinFeatureMode = settlement.FreeSpinFeatureMode,
            BonusRound = settlement.BonusRound,
            QueuedBonusRounds = settlement.QueuedBonusRounds,
            AwardedBonusRounds = settlement.AwardedBonusRounds
        };

    private string? SessionToken() => AccountSessionCookie.Read(Request);
}
