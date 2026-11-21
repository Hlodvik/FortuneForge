using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace FortuneForge.Games.NeonNights.Server;

public static class NeonCarRespinEndpoints
{
    public const string RoutePrefix = "/api/slots/neon";
    public const string RespinRoute = "/car-respin";
    public const string RespinNitroRoute = "/car-respin/nitro-choice";
    public const string DismissRoute = "/car-respin/dismiss";
    public const string AuditGameId = "neon-nights";

    public static IEndpointRouteBuilder MapNeonCarRespin(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, CancellationToken, Task<string?>> resolveAccount, string? writeRateLimitPolicy = null)
    {
        var group = endpoints.MapGroup(RoutePrefix)
            .WithMetadata(new GameWorkerPublicRoute(NeonArcadeSlotDefinitions.GameId, RoutePrefix, AuditGameId));
        if (writeRateLimitPolicy is not null) group.RequireRateLimiting(writeRateLimitPolicy);

        group.MapPost(RespinRoute, (NeonCarReelRequest request, [FromServices] NeonCarRespinStore store,
            HttpContext context, CancellationToken token) => Resolve(request, store, context, token, false));
        group.MapPost(RespinNitroRoute, (NeonCarReelRequest request, [FromServices] NeonCarRespinStore store,
            HttpContext context, CancellationToken token) => Resolve(request, store, context, token, true));
        group.MapPost(DismissRoute, async (NeonCarDismissRequest request, [FromServices] NeonCarRespinStore store,
            HttpContext context, CancellationToken token) =>
        {
            var account = await resolveAccount(context, token);
            if (account is null) return SignIn();
            if (request.SpinId == Guid.Empty) return Results.BadRequest(new { error = "Choose the completed Car Play." });
            await store.DismissAsync(account, request.SpinId, token);
            return Results.NoContent();
        });
        return endpoints;

        async Task<IResult> Resolve(NeonCarReelRequest request, NeonCarRespinStore store, HttpContext context,
            CancellationToken token, bool isNitro)
        {
            var account = await resolveAccount(context, token);
            if (account is null) return SignIn();
            if (request.SpinId == Guid.Empty || request.ReelIndex is not { } reel || !NeonNitroChoice.IsChoiceReel(reel))
                return Results.BadRequest(new { error = isNitro ? "Choose one of the five reels for Nitro Wild." :
                    "Choose an available reel from the latest Car Play." });
            try
            {
                var result = isNitro ? await store.ChooseNitroAsync(account, request.SpinId, reel, token)
                    : await store.RespinAsync(account, request.SpinId, reel, token);
                return result.PendingNeonNitroChoice is { } pending
                    ? Results.Json(new { pendingNeonNitroChoice = pending }, statusCode: 202) : Results.Ok(result);
            }
            catch (NeonCarActionConflictException error)
            {
                return isNitro ? Results.Conflict(new { code = "nitro-choice-conflict", error = error.Message })
                    : Results.Conflict(new { error = error.Message });
            }
            catch (GameWalletUnavailableException error)
            {
                return Results.Json(new { error = error.Message }, statusCode: 503);
            }
        }
    }

    private static IResult SignIn() => Results.Json(new { error = "Sign in to play Fortune Slots." }, statusCode: 401);
}
