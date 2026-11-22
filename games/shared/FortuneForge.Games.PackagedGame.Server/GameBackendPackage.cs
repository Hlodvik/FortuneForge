using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.PackagedGame.Server;

public sealed class GameBackendPackage : IGameBackendPackage
{
    public string GameId => GameWorkerModule.GameId;

    public WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = WebApplication.CreateBuilder(options);
        configure(builder);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32 * 1024);

        var hosted = builder.Configuration["GameWorker:Mode"] switch
        {
            null or "emulator" => false,
            "hosted" => true,
            _ => throw new InvalidOperationException("GameWorker:Mode must be emulator or hosted."),
        };

        if (hosted && string.IsNullOrWhiteSpace(builder.Configuration["GameWorker:SigningKey"]))
            throw new InvalidOperationException("Hosted game workers require GameWorker:SigningKey.");
        if (hosted && int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var cloudRunPort))
            builder.WebHost.UseUrls($"http://0.0.0.0:{cloudRunPort}");

        var localAccount = hosted ? null : DemoGameAccount.Create(builder.Configuration);
        var database = hosted ? GameWorkerFirestore.CreateHostedDatabase(builder.Configuration, builder.Environment) : localAccount?.Database;
        if (database is not null)
            builder.Services.AddSingleton(database);
        GameWorkerModule.Configure(builder);

        var app = builder.Build();
        app.UseRouting();
        if (hosted)
            app.UseGameWorkerAuthentication(GameWorkerModule.GameId);
        else if (localAccount is not null)
            app.UseLocalGameWorkerCommandDigests(DemoGameAccount.IsLocalRequest);
        app.MapGet("/healthz", () => Results.Ok(new
        {
            gameId = GameWorkerModule.GameId,
            contractVersion = 1,
            emulatorConfigured = localAccount is not null ||
                hosted && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST"))
        }));

        app.Use(async (context, next) =>
        {
            var publicRoute = context.GetEndpoint()?.Metadata.GetMetadata<GameWorkerPublicRoute>();
            if (!context.Request.Path.StartsWithSegments("/api/games") && publicRoute is null)
            {
                await next(context);
                return;
            }
            if (publicRoute is not null && (publicRoute.WorkerGameId != GameWorkerModule.GameId ||
                !context.Request.Path.StartsWithSegments(publicRoute.PathPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                await Results.NotFound().ExecuteAsync(context);
                return;
            }

            if (!hosted && localAccount is null)
            {
                await Results.Json(new { error = "A local emulator and demo account are required." },
                    statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
                return;
            }

            if ((!hosted && !DemoGameAccount.IsLocalRequest(context)) ||
                context.Request.Headers["X-FortuneForge-Practice"].Any(value =>
                    string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)))
            {
                await Results.StatusCode(StatusCodes.Status403Forbidden).ExecuteAsync(context);
                return;
            }

            if (hosted)
            {
                var userId = GameWorkerAuthentication.GetAccountId(context);
                if (string.IsNullOrWhiteSpace(userId) || userId.Length > 256 ||
                    userId.Contains('/') || userId.Contains('\\') || userId.Any(char.IsControl))
                {
                    await Results.StatusCode(StatusCodes.Status403Forbidden).ExecuteAsync(context);
                    return;
                }

                // The host records its durable request intent before forwarding the action.
                // A detached worker must carry that same observation into the game transaction.
                if ((context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE") &&
                    app.Configuration.GetValue("GameSecurityRequests:Enabled", true))
                {
                    var observation = GameSecurityContext.Current;
                    if (observation is null || observation.GameId != (publicRoute?.AuditGameId ?? GameWorkerModule.GameId) ||
                        observation.Method != context.Request.Method || observation.Path != context.Request.Path.Value)
                    {
                        await Results.Json(new { error = "game-recording-unavailable" },
                            statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
                        return;
                    }
                }
            }

            await next(context);
        });

        GameWorkerModule.Map(app, hosted ? GameWorkerAuthentication.GetAccountId : _ => localAccount?.UserId);
        return app;
    }
}
