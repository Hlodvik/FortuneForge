using FortuneForge.Games.PackagedGame.Server;

var builder = WebApplication.CreateBuilder(args);
var account = DemoGameAccount.Create(builder.Configuration);
if (account is not null)
    builder.Services.AddSingleton(account.Database);
GameWorkerModule.Configure(builder);

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new
{
    gameId = GameWorkerModule.GameId,
    contractVersion = 1,
    emulatorConfigured = account is not null
}));

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/games"))
    {
        if (account is null)
        {
            await Results.Json(new { error = "A local emulator and demo account are required." },
                statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
            return;
        }
        if (!DemoGameAccount.IsLocalRequest(context) ||
            context.Request.Headers["X-FortuneForge-Practice"].Any(value =>
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)))
        {
            await Results.StatusCode(StatusCodes.Status403Forbidden).ExecuteAsync(context);
            return;
        }
    }
    await next(context);
});

GameWorkerModule.Map(app, account);
app.Run();
