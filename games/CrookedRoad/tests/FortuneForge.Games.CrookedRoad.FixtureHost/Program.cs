using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.CrookedRoad;
using FortuneForge.Games.CrookedRoad.Server;
using FortuneForge.Games.CrookedRoad.Tests.Support;
using Google.Api.Gax;
using Google.Cloud.Firestore;

var host = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
if (host is null || !Uri.TryCreate("http://" + host, UriKind.Absolute, out var emulator) || !emulator.IsLoopback)
    throw new InvalidOperationException("The paid-protocol fixture requires a loopback Firestore emulator; live Firestore is forbidden.");
var portArgument = args.FirstOrDefault(argument => argument.StartsWith("--port="));
var port = portArgument is null ? 5299 : int.Parse(portArgument["--port=".Length..]);
if (port is < 1024 or > 65535) throw new ArgumentException("Choose a fixture port from 1024 to 65535.");
var account = "crooked-road-local-paid-test";
var database = new FirestoreDbBuilder { ProjectId = "crooked-road-browser-" + Guid.NewGuid().ToString("N"), EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
var wallet = new CrookedRoadTestWallet();
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.Configuration["CrookedRoad:AllowNewPaidRounds"] = "true";
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<IGameWalletClient>(wallet);
builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
builder.Services.AddCrookedRoadPaid();
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/api/slots/state")
    {
        var gameIds = context.Request.Query["gameId"];
        if (gameIds.Count != 1 || gameIds[0] != CrookedRoadRules.GameId)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "This local fixture serves The Crooked Road only." });
            return;
        }
        // Adapt only the host transport path. The native paid state handler remains the sole owner.
        context.Request.Path = CrookedRoadPaidRouteContract.StatePath;
    }
    if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == CrookedRoadPaidRouteContract.StatePath)
        context.Response.Headers["X-FortuneForge-Prepared-Spin"] = "1";
    await next(context);
});
app.UseRouting();
app.MapCrookedRoadPaidRounds(_ => account);
app.MapGet("/__fixture/health", () => Results.Ok(new { localOnly = true, gameId = CrookedRoadRules.GameId,
    engineVersion = CrookedRoadRules.EngineVersion, database.ProjectId, account }));
async Task<IResult> CurrentAccount()
{
    var state = await wallet.GetStateAsync(account, default);
    return Results.Ok(new { userId = account, playerName = "Local paid-protocol test", email = "paid-test@example.test",
        createdAtUtc = "2026-01-01T00:00:00Z", role = "player", balances = new { slotsCredits = state.SlotsCreditsBalanceCents / 100m, freeGames = 0 },
        slots = new { spinsPlayed = wallet.UniqueSettlements, wins = 0, losses = 0, creditsWagered = 0, creditsWon = 0, netCredits = 0 } });
}
app.MapGet("/__fixture/account", CurrentAccount);
app.MapGet("/api/accounts/me", CurrentAccount);
app.Run();
