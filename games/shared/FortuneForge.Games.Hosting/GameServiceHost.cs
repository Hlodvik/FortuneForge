using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Games.Hosting;

public static class GameServiceHost
{
    public static WebApplication Create(string[] args, string gameId, string engineVersion)
    {
        var app = WebApplication.CreateBuilder(args).Build();
        app.MapGet("/healthz", () => Results.Ok(new { gameId, engineVersion }));
        return app;
    }

    public static void MapDemoAction<TRequest, TOutcome>(
        this WebApplication app, string route, Func<TRequest, TOutcome> action)
    {
        app.MapPost(route, ([FromBody] TRequest request) =>
        {
            try
            {
                return Results.Ok(action(request));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
    }
}
