using FortuneForge.Games.RobotRevolution;
using FortuneForge.Games.RobotRevolution.Server;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Controllers;

namespace FortuneForge.Server.Slots.Games;

internal static class FifthSunDemoEndpoints
{
    private sealed class ScopedDemoRoute { }

    internal static bool IsScopedRoute(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<ScopedDemoRoute>() is not null;

    internal static void MapFifthSunDemo(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/slots/demo/status", (string gameId) => gameId == RobotRevolutionEngine.GameId
                ? Results.NoContent() : Results.NotFound())
            .WithMetadata(new ScopedDemoRoute());
        endpoints.MapPost("/api/slots/demo/spins", Spin)
            .WithMetadata(new ScopedDemoRoute())
            .RequireRateLimiting(RateLimitPolicies.SlotSpins);
    }

    private static IResult Spin(DemoSpinRequest request)
    {
        if (request.GameId != RobotRevolutionEngine.GameId) return Results.NotFound();
        if (request.UseFreeSpin || request.FreeSpinsRemaining != 0 || request.FreeSpinWagerPoints is not null ||
            request.FreeSpinFeatureMode is not null || request.EnergyBalance != 0 || request.SealCollections is { Count: > 0 } ||
            request.MidnightBonusState is not null || request.CrucibleWageredCents != 0 || request.OlympusTumbleShowcase is not null ||
            request.NeonDiscoSpinsRemaining != 0 || request.NeonDiscoMultiplier is not null || request.NeonDiscoWagerPoints is not null)
            return Results.BadRequest(new { error = "The Sunstone Eclipse demo accepts current paid rounds and Eclipse state only." });
        try
        {
            return Results.Json(FifthSunDemo.Spin(request.WagerPoints, request.PaidMode));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
    }
}
