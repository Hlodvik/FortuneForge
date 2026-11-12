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

    public static void MapGameAction<TRequest, TOutcome>(
        this WebApplication app, string route, Func<TRequest, TOutcome> action,
        Func<TRequest, string>? accountSelector = null)
    {
        app.MapPost(route, ([FromBody] TRequest request, HttpContext context) =>
        {
            try
            {
                var verified = GameWorkerAuthentication.GetAccountId(context);
                if (verified is not null && accountSelector is not null && verified != accountSelector(request))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                return Results.Ok(action(request));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
    }
}
