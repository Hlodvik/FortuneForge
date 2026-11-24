using System.Net;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.CosmicFortune;
using FortuneForge.Games.CosmicFortune.LocalHarness;
using FortuneForge.Games.CosmicFortune.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using Google.Api.Gax;
using Google.Cloud.Firestore;

var host = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
if (host is null || !Uri.TryCreate("http://" + host, UriKind.Absolute, out var emulator) || !emulator.IsLoopback)
    throw new InvalidOperationException("The local paid harness requires a loopback Firestore emulator.");
const string account = "pharaoh-ascent-local-paid-test";
const string projectPrefix = "demo-pharaoh-paid-";
var project = Environment.GetEnvironmentVariable("PHARAOH_LOCAL_TEST_PROJECT_ID");
var walletSnapshot = Environment.GetEnvironmentVariable("PHARAOH_LOCAL_WALLET_SNAPSHOT");
if (project is not null && (!project.StartsWith(projectPrefix, StringComparison.Ordinal) ||
    project.Length == projectPrefix.Length || project.Any(character => !char.IsAsciiLetterLower(character) &&
        !char.IsAsciiDigit(character) && character != '-')))
    throw new InvalidOperationException("The local paid harness project must start with demo-pharaoh-paid- and contain only lowercase letters, digits, and hyphens.");
if ((project is null) != (walletSnapshot is null) || walletSnapshot is not null && string.IsNullOrWhiteSpace(walletSnapshot))
    throw new InvalidOperationException("Restore the local test project and wallet snapshot together using PHARAOH_LOCAL_TEST_PROJECT_ID and PHARAOH_LOCAL_WALLET_SNAPSHOT.");
var database = new FirestoreDbBuilder
{
    ProjectId = project ?? projectPrefix + Guid.NewGuid().ToString("N"),
    EmulatorDetection = EmulatorDetection.EmulatorOnly,
}.Build();
var wallet = new LocalTestWallet(account, walletSnapshot);
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5301");
builder.Configuration["CosmicFortune:AllowNewPaidRounds"] = "true";
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IGameRandomSource, CryptographicSlotRandom>();
builder.Services.AddSingleton<IGameWalletClient>(wallet);
builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
builder.Services.AddSingleton<CosmicFortuneRoundStore>();
builder.Services.AddSingleton<CosmicFortuneRoundService>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
    { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
    context.Response.Headers.CacheControl = "no-store";
    if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/api/slots/state")
    {
        var ids = context.Request.Query["gameId"];
        if (ids.Count != 1 || ids[0] != CosmicFortuneEngine.GameId)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "This local paid harness serves Pharaoh's Ascent only." });
            return;
        }
        context.Request.Path = CosmicFortunePaidRouteContract.StatePath;
    }
    if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == CosmicFortunePaidRouteContract.StatePath)
        context.Response.Headers["X-FortuneForge-Prepared-Spin"] = "1";
    await next();
});
app.UseRouting();
app.MapCosmicFortunePaidRounds(_ => account);
app.MapGet("/__fixture/health", () => Results.Ok(new { localOnly = true, productionEngine = true,
    testFundsOnly = true, gameId = CosmicFortuneEngine.GameId, engineVersion = CosmicFortuneEngine.EngineVersion,
    database.ProjectId, account }));
app.MapGet("/__fixture/wallet", () => Results.Ok(wallet.Snapshot()));
async Task<IResult> CurrentAccount()
{
    var state = await wallet.GetStateAsync(account, default);
    return Results.Ok(new { userId = account, playerName = "Local Pharaoh test", email = "pharaoh-test@example.test",
        createdAtUtc = "2026-01-01T00:00:00Z", role = "player",
        balances = new { slotsCredits = state.SlotsCreditsBalanceCents / 100m, freeGames = 0 },
        slots = new { spinsPlayed = 0, wins = 0, losses = 0, creditsWagered = 0, creditsWon = 0, netCredits = 0 } });
}
app.MapGet("/__fixture/account", CurrentAccount);
app.MapGet("/api/accounts/me", CurrentAccount);
app.Run();
