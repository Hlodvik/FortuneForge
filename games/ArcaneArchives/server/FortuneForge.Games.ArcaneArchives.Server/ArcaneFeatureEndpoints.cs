using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace FortuneForge.Games.ArcaneArchives.Server;

public static class ArcaneFeatureEndpoints
{
    public const string RoutePrefix = "/api/slots/arcane";
    public const string AuditGameId = "arcane-archives";
    public const string PotionRoute = "/potions/activate";
    public const string TomeRoute = "/tome";
    public const string PreparedSpinRoute = "/prepared-spins";

    public static IEndpointRouteBuilder MapArcaneFeatures(this IEndpointRouteBuilder endpoints,
        Func<HttpContext, CancellationToken, Task<string?>> resolveAccount, string? writeRateLimitPolicy = null)
    {
        var api = endpoints.MapGroup(RoutePrefix)
            .WithMetadata(new GameWorkerPublicRoute(ArcaneArchivesEngine.GameId, RoutePrefix, AuditGameId));
        if (writeRateLimitPolicy is not null) api.RequireRateLimiting(writeRateLimitPolicy);

        api.MapPost(PotionRoute, async (ArcanePotionActivationRequest request, [FromServices] ArcaneFeatureStore features,
            HttpContext context, CancellationToken token) =>
        {
            var userId = await resolveAccount(context, token);
            if (userId is null)
                return Results.Json(new { error = "Sign in to use an Arcane potion." }, statusCode: 401);
            if (request.PotionId <= 0)
                return Results.BadRequest(new { error = "Choose a bottled Arcane potion." });
            try
            {
                var state = await features.ActivatePotionAsync(userId, request.PotionId, DateTime.UtcNow, token);
                return Results.Ok(ArcanePotionView.FromState(state));
            }
            catch (ArcanePotionConflictException exception)
            {
                return Results.Conflict(new { code = "arcane-potion-conflict", error = exception.Message });
            }
        });

        api.MapPost(TomeRoute, async (ArcaneTomeRequest request, [FromServices] ArcaneFeatureStore features,
            HttpContext context, CancellationToken token) =>
        {
            var userId = await resolveAccount(context, token);
            if (userId is null)
                return Results.Json(new { error = "Sign in to use the Arcane Tome." }, statusCode: 401);
            try
            {
                var state = await features.SelectTomePageAsync(userId, request.Page, request.Activate, DateTime.UtcNow, token);
                return Results.Ok(ArcaneTome.ToView(state));
            }
            catch (ArcaneTomeConflictException exception)
            {
                return Results.Conflict(new { code = "arcane-tome-conflict", error = exception.Message });
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
        return endpoints;
    }
}
