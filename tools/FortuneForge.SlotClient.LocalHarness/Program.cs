using System.Net;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.PiratesFortune.Server;
using FortuneForge.Games.GodsOfOlympus.Server;
using FortuneForge.Games.JadeDragon.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using FortuneForge.SlotClient.LocalHarness;
using Google.Api.Gax;
using Google.Cloud.Firestore;

var selected = Environment.GetEnvironmentVariable("SLOT_REVIEW_GAME") ?? throw new InvalidOperationException("Select a slot review game.");
var (gameId, prefix, statePath, port) = selected switch
{
    "pirates" => ("pirates-fortune-v1", "PiratesFortune", PiratesPaidRouteContract.StatePath, 5302),
    "olympus" => ("gods-of-olympus-v1", "GodsOfOlympus", OlympusPaidRouteContract.StatePath, 5303),
    "jade" => ("empty-slots-v1", "JadeDragon", JadePaidRouteContract.StatePath, 5304),
    _ => throw new InvalidOperationException("Unknown isolated test game.")
};
var host = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
if (host is null || !Uri.TryCreate("http://" + host, UriKind.Absolute, out var emulator) || !emulator.IsLoopback)
    throw new InvalidOperationException("A loopback Firestore emulator is required. Real databases are forbidden.");
var database = new FirestoreDbBuilder { ProjectId = "demo-slot-client-" + selected + "-" + Guid.NewGuid().ToString("N"), EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
var account = selected + "-isolated-paid-player";
var wallet = new LocalTestWallet(gameId, account);
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:" + port);
builder.Configuration[prefix + ":AllowNewPaidRounds"] = "true";
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IGameRandomSource, CryptographicSlotRandom>();
builder.Services.AddSingleton<IGameWalletClient>(wallet);
builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
switch (selected)
{
    case "pirates": builder.Services.AddSingleton<PiratesRoundStore>(); builder.Services.AddSingleton<PiratesRoundService>(); break;
    case "olympus": builder.Services.AddSingleton<OlympusRoundStore>(); builder.Services.AddSingleton<OlympusRoundService>(); break;
    case "jade": builder.Services.AddSingleton<JadeRoundStore>(); builder.Services.AddSingleton<JadeRoundService>(); break;
}
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip)) { context.Response.StatusCode = 403; return; }
    context.Response.Headers.CacheControl = "no-store";
    if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/api/slots/state")
    {
        if (context.Request.Query["gameId"] != gameId) { context.Response.StatusCode = 400; return; }
        context.Request.Path = statePath;
    }
    if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path == statePath) context.Response.Headers["X-FortuneForge-Prepared-Spin"] = "1";
    if (HttpMethods.IsPost(context.Request.Method) && context.Request.Headers["X-FortuneForge-Expected-Account"] != account) { context.Response.StatusCode = 403; return; }
    await next();
});
app.UseRouting();
switch (selected)
{
    case "pirates": app.MapPiratesPaidRounds(_ => account); break;
    case "olympus": app.MapOlympusPaidRounds(_ => account); break;
    case "jade": app.MapJadePaidRounds(_ => account); app.MapGet("/api/slots/jade-dragon/presentation", () => Results.Ok(JadeDragonPresentation.Create())); break;
}
app.MapGet("/__fixture/health", () => Results.Ok(new { localOnly = true, productionEngine = true, testFundsOnly = true, gameId, account, database.ProjectId }));
app.MapGet("/__fixture/wallet", () => Results.Ok(wallet.Snapshot()));
app.MapGet("/__fixture/account", async () =>
{
    var state = await wallet.GetStateAsync(account, default);
    return Results.Ok(new { userId = account, role = "player", balances = new { slotsCredits = state.SlotsCreditsBalanceCents / 100m } });
});
app.Run();
