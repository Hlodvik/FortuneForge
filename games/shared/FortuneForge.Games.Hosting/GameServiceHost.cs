using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace FortuneForge.Games.Hosting;

public static class GameServiceHost
{
    public static WebApplication Create(string[] args, string gameId, string engineVersion)
    {
        var app = WebApplication.CreateBuilder(args).Build();
        app.MapGet("/healthz", () => Results.Ok(new { gameId, engineVersion }));
        return app;
    }
}
