// Edited: 2026-11-18T17:59:35-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:30:13-06:00 | Computer: Vinnabok
using System.Text.Json.Serialization;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.ArcaneArchives.Server;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Games.NeonNights.Server;
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? JadeDragonEngineVersion = null);

[ApiController]
[Route("api/slots")]
public sealed class SlotsController(
    SpinService spinService,
    AccountService accountService,
    ILogger<SlotsController> logger,
    GameWorkerClient? workers = null,
    IConfiguration? configuration = null) : ControllerBase
{
    private bool ArcaneWorkerRoundsEnabled => configuration?.GetValue<bool>("Slots:ArcaneWorkerRoundsEnabled") == true;
    private bool NeonWorkerRoundsEnabled => GetPreparedWorkerRoute(NeonArcadeSlotDefinitions.GameId) is not null;

    private PreparedSlotWorkerRoute? GetPreparedWorkerRoute(string gameId) =>
        PreparedSlotWorkerRegistry.Find(gameId) is { } route && configuration?.GetValue<bool>(route.EnabledSetting) == true
            ? route : null;

    [HttpPost("~" + NeonPrimaryRouteContract.AdminPath)]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> AdminNeonTrigger(
        NeonPrimaryAdminCommand request,
        CancellationToken cancellationToken)
    {
        var account = await accountService.GetAccessContextAsync(SessionToken(), cancellationToken);
        if (account is null)
            return Unauthorized(new { error = "Sign in to play Fortune Slots." });
        if (!IsExpectedAccount(account.UserId))
            return WrongAccount();
        if (!account.IsAdmin)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "Only administrators can start a Neon preview round." });

        return await ForwardNeonCommandAsync(account.UserId, NeonPrimaryRouteContract.AdminPath, request,
            cancellationToken, GameSecurityContext.Current?.RequestId ?? Guid.NewGuid().ToString("N"));
    }

    [HttpGet("state")]
    [EnableRateLimiting(RateLimitPolicies.SlotReads)]
    public async Task<ActionResult> State(
        [FromQuery] string gameId,
        CancellationToken cancellationToken)
    {
        try
        {
            var forwardArcane = gameId == ArcaneArchivesEngine.GameId && ArcaneWorkerRoundsEnabled;
            var preparedWorker = GetPreparedWorkerRoute(gameId);
            if (!forwardArcane && preparedWorker is null)
                spinService.ValidateGame(gameId);
            var accountResult = await accountService.GetProfileAsync(SessionToken(), cancellationToken);
            if (accountResult.Value is null)
            {
                return Unauthorized(new { error = "Sign in to play Fortune Slots." });
            }
            if (!IsExpectedAccount(accountResult.Value.UserId))
                return WrongAccount();

            if (forwardArcane)
            {
                Response.Headers["X-FortuneForge-Prepared-Spin"] = "1";
                return await ArcanePaidWorkerGateway.StateAsync(workers, HttpContext, accountResult.Value.UserId, cancellationToken);
            }
            if (preparedWorker is not null)
            {
                Response.Headers["X-FortuneForge-Prepared-Spin"] = "1";
                return await SlotWorkerGateway.ReadStateAsync(workers, HttpContext, gameId, accountResult.Value.UserId,
                    preparedWorker.StatePath, cancellationToken);
            }
            return Ok(await accountService.GetSlotStateAsync(
                accountResult.Value.UserId,
                gameId,
                spinService.GetPointValueInCents(gameId),
                cancellationToken));
        }
        catch (GameWorkerUnavailableException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = exception.Message });
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

    [HttpPost("~" + NeonCarRespinEndpoints.RoutePrefix + NeonCarRespinEndpoints.RespinRoute)]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> RespinNeonCarReel(NeonCarReelRequest request, CancellationToken cancellationToken)
    {
        var account = await accountService.GetAccessContextAsync(SessionToken(), cancellationToken);
        if (account is null)
            return Unauthorized(new { error = "Sign in to play Fortune Slots." });
        if (!IsExpectedAccount(account.UserId))
            return WrongAccount();
        return await ForwardNeonCommandAsync(account.UserId,
            NeonCarRespinEndpoints.RoutePrefix + NeonCarRespinEndpoints.RespinRoute, request, cancellationToken);
    }

    [HttpPost("~" + NeonPrimaryRouteContract.NitroPath)]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> ChooseNeonNitroReel(NeonPrimaryNitroCommand request, CancellationToken cancellationToken) =>
        ResolveNitroChoice(request, fromRespin: false, cancellationToken);

    [HttpPost("~" + NeonCarRespinEndpoints.RoutePrefix + NeonCarRespinEndpoints.RespinNitroRoute)]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public Task<ActionResult> ChooseNeonRespinNitroReel(NeonPrimaryNitroCommand request, CancellationToken cancellationToken) =>
        ResolveNitroChoice(request, fromRespin: true, cancellationToken);

    private async Task<ActionResult> ResolveNitroChoice(
        NeonPrimaryNitroCommand request, bool fromRespin, CancellationToken cancellationToken)
    {
        var account = await accountService.GetAccessContextAsync(SessionToken(), cancellationToken);
        if (account is null)
            return Unauthorized(new { error = "Sign in to play Fortune Slots." });
        if (!IsExpectedAccount(account.UserId))
            return WrongAccount();
        var route = fromRespin
            ? NeonCarRespinEndpoints.RoutePrefix + NeonCarRespinEndpoints.RespinNitroRoute
            : NeonPrimaryRouteContract.NitroPath;
        var forwarded = await ForwardNeonCommandAsync(account.UserId, route, request, cancellationToken);
        if (fromRespin || !SlotWorkerGateway.IsConflict(forwarded, NeonPrimaryRouteContract.LegacyNitroCode))
            return forwarded;

        try
        {
            if (request.SpinId == Guid.Empty || request.ReelIndex is not { } reelIndex || reelIndex is < 0 or >= 5)
                return BadRequest(new { error = "Choose one of the five reels for Nitro Wild." });
            var finalized = await accountService.FinalizeNeonNitroChoiceAsync(
                account.UserId, request.SpinId, reelIndex, cancellationToken);
            return Ok(ApplySettlement(finalized.Result, finalized.Settlement,
                finalized.Result.IsFreeSpin, finalized.Result.ChargedWagerCents));
        }
        catch (NeonNitroChoiceConflictException exception)
        {
            return Conflict(new { code = "nitro-choice-conflict", error = exception.Message });
        }
    }

    [HttpPost("~" + NeonCarRespinEndpoints.RoutePrefix + NeonCarRespinEndpoints.DismissRoute)]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> DismissNeonCarRespin(NeonCarDismissRequest request, CancellationToken cancellationToken)
    {
        var account = await accountService.GetAccessContextAsync(SessionToken(), cancellationToken);
        if (account is null)
            return Unauthorized(new { error = "Sign in to play Fortune Slots." });
        if (!IsExpectedAccount(account.UserId))
            return WrongAccount();
        return await ForwardNeonCommandAsync(account.UserId,
            NeonCarRespinEndpoints.RoutePrefix + NeonCarRespinEndpoints.DismissRoute, request, cancellationToken);
    }

    private async Task<ContentResult> ForwardNeonCommandAsync<TCommand>(string accountId, string path, TCommand command,
        CancellationToken cancellationToken, string? operationId = null)
    {
        try
        {
            if (!NeonWorkerRoundsEnabled)
                throw new GameWorkerUnavailableException(NeonArcadeSlotDefinitions.GameId);
            return await SlotWorkerGateway.PostAsync(workers, HttpContext, NeonArcadeSlotDefinitions.GameId,
                accountId, path, command, operationId, cancellationToken);
        }
        catch (GameWorkerUnavailableException exception)
        {
            return new ContentResult
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable,
                ContentType = "application/json",
                Content = System.Text.Json.JsonSerializer.Serialize(new { error = exception.Message })
            };
        }
    }

    [HttpPost("spins")]
    [HttpPost(PreparedSlotWorkerRegistry.PublicSpinRoute)]
    [HttpPost("arcane" + ArcaneFeatureEndpoints.PreparedSpinRoute)]
    [EnableRateLimiting(RateLimitPolicies.SlotSpins)]
    public async Task<ActionResult> Spin(
        SpinRequest request,
        CancellationToken cancellationToken)
    {
        var preparedOperationPending = false;
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
            var isArcane = request.GameId == ArcaneArchivesEngine.GameId;
            var preparedWorker = GetPreparedWorkerRoute(request.GameId);
            var preparedPath = PreparedSlotWorkerRegistry.IsPreparedPath(Request.Path);
            if (!IsExpectedAccount(account.UserId))
                return WrongAccount();
            if (preparedPath && preparedWorker is null)
                throw new GameWorkerUnavailableException(request.GameId);
            string? operationId = null;
            if ((isArcane || preparedWorker is not null) && Request.Headers.TryGetValue("Idempotency-Key", out var keys))
            {
                if (keys.Count != 1 || !SlotPreparedOperationRequest.IsValidId(keys[0]))
                    return BadRequest(new { code = "invalid-spin-operation", error = "Include one valid spin operation ID." });
                operationId = keys[0];
            }
            if (ArcanePaidWorkerGateway.IsPreparedPath(Request.Path) && (!isArcane || operationId is null))
                return BadRequest(new { code = "invalid-spin-operation", error = "Include one valid Arcane spin operation ID." });
            if (preparedPath && operationId is null)
                return BadRequest(new { code = "invalid-spin-operation", error = "Include one valid spin operation ID." });
            if (preparedWorker is not null)
            {
                var response = await SlotWorkerGateway.PostAsync(workers, HttpContext, request.GameId, account.UserId,
                    preparedPath ? PreparedSlotWorkerRegistry.PublicSpinPath : preparedWorker.SpinPath, request,
                    operationId ?? GameSecurityContext.Current?.RequestId ?? Guid.NewGuid().ToString("N"), cancellationToken);
                if (!SlotWorkerGateway.IsConflict(response, FortuneForge.Games.Abstractions.LegacySlotAdmissionPendingException.Code))
                    return response;

                var legacy = await accountService.ReconcileExpiredLegacySlotAsync(
                    account.UserId, request.GameId, DateTime.UtcNow, cancellationToken);
                if (legacy == LegacySlotRecoveryResult.Active)
                {
                    Response.Headers.RetryAfter = "1";
                    return StatusCode(StatusCodes.Status429TooManyRequests, new
                    {
                        code = "legacy-slot-round-active",
                        error = "Wait for your previous spin to finish.",
                        retryAfterMilliseconds = 1000
                    });
                }
                if (legacy is LegacySlotRecoveryResult.Reconciled or LegacySlotRecoveryResult.NoLegacyAdmission)
                    return Conflict(new
                    {
                        code = "legacy-slot-round-restored",
                        error = legacy == LegacySlotRecoveryResult.Reconciled
                            ? "The interrupted spin was closed and any spent feature credits were restored. You can spin again."
                            : "Your previous spin has closed. Check the updated game state before spinning again."
                    });
                return Conflict(new
                {
                    code = "legacy-slot-round-review",
                    error = "Your previous spin needs to be reconciled before you can spin again."
                });
            }
            if (!isArcane)
            {
                if (PreparedSlotWorkerRegistry.Find(request.GameId) is null)
                    throw new KeyNotFoundException($"Unknown slot game '{request.GameId}'.");
                throw new GameWorkerUnavailableException(request.GameId);
            }

            if (!ArcaneWorkerRoundsEnabled)
                throw new GameWorkerUnavailableException(request.GameId);
            var forwarded = await ArcanePaidWorkerGateway.SpinAsync(workers, HttpContext, account.UserId,
                new(request.GameId, request.WagerPoints, request.UseFreeSpin, request.UseSpecialBoost), operationId, cancellationToken);
            if (forwarded is not null) return forwarded;
            if (operationId is null)
                throw new GameWorkerUnavailableException(request.GameId);

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
                TimeSpan.Zero,
                cancellationToken,
                operationId: operationId,
                allowNewPrepared: false);
            var result = admission.ResolvedOutcome ??
                throw new InvalidOperationException("The original Arcane spin outcome is unavailable.");
            preparedOperationPending = true;
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
        catch (SlotPreparedOperationConflictException exception)
        {
            return Conflict(new { code = "slot-operation-conflict", error = exception.Message, pendingOperation = exception.PendingOperation });
        }
        catch (SlotPreparedStartPausedException exception)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { code = "slot-prepared-start-paused", error = exception.Message });
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
                required = exception.Required,
                preparedOperationPending
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
        catch (ArcaneTomeActivationRequiredException exception)
        {
            return Conflict(new { code = "arcane-tome-required", error = exception.Message });
        }
        catch (ArcanePotionWagerLockedException exception)
        {
            return Conflict(new { code = "arcane-potion-wager-locked", error = exception.Message,
                lockedWagerPoints = exception.LockedWagerPoints });
        }
        catch (NeonNitroChoiceConflictException exception)
        {
            return Conflict(new { code = "nitro-choice-required", error = exception.Message });
        }
        catch (NeonDiscoWagerLockedException exception)
        {
            return Conflict(new { code = "neon-disco-wager-locked", error = exception.Message,
                lockedWagerPoints = exception.LockedWagerPoints });
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

    private bool IsExpectedAccount(string accountId) => GameAccountBinding.Matches(Request, accountId);

    private ObjectResult WrongAccount() => StatusCode(StatusCodes.Status403Forbidden,
        new { code = "slot-session-changed", error = "Sign back in to the account that opened this game." });

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
            NeonDiscoWagerPoints = settlement.NeonDiscoWagerPoints,
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
            ArcaneTome = ArcaneTome.ToView(settlement.ArcaneTome),
            ArcanePotions = settlement.ArcanePotions is { } potions ? ArcanePotionView.FromState(potions) : null,
            ArcaneIngredientsAwarded = settlement.ArcaneIngredientsAwarded,
            BonusRound = settlement.BonusRound,
            QueuedBonusRounds = settlement.QueuedBonusRounds,
            AwardedBonusRounds = settlement.AwardedBonusRounds
        };

    private string? SessionToken() => AccountSessionCookie.Read(Request);
}
