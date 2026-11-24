using System.Net;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.FrontierFortune.LocalHarness;
using FortuneForge.Games.FrontierFortune.Server;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;

const string emulator = "127.0.0.1:8873";
const string project = "demo-frontier-paid-validation";
const string reviewAccount = "frontier-outlaw-paid-review-v2";
const string qaAccount = "frontier-outlaw-paid-validation-v2";
const string accountHeader = "X-Frontier-Local-Account";
if (Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") != emulator)
    throw new InvalidOperationException("This test-only harness requires FIRESTORE_EMULATOR_HOST=127.0.0.1:8873.");
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5279");
builder.Configuration["FrontierFortune:AllowNewPaidRounds"] = "true";
var database = new FirestoreDbBuilder { ProjectId = project, EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
builder.Services.AddSingleton(database);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IGameRandomSource, FrontierCryptographicRandom>();
builder.Services.AddSingleton<IFrontierRoundStore, FrontierRoundStore>();
builder.Services.AddSingleton<FrontierHarnessWallet>();
builder.Services.AddSingleton<IGameWalletClient>(services => services.GetRequiredService<FrontierHarnessWallet>());
builder.Services.AddSingleton<IGameWalletAccountReader>(services => services.GetRequiredService<FrontierHarnessWallet>());
builder.Services.AddSingleton<FrontierRoundService>();
var app = builder.Build();
var selectedAccountKey = new object();
app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
    { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
    var selector = "review";
    if (context.Request.Headers.TryGetValue(accountHeader, out var values))
    {
        if (values.Count != 1 || values[0] is not ("review" or "qa"))
        {
            await Results.BadRequest(new { error = "Choose the review or qa local account." }).ExecuteAsync(context);
            return;
        }
        selector = values[0]!;
    }
    context.Items[selectedAccountKey] = selector == "qa" ? qaAccount : reviewAccount;
    await next();
});
app.MapGet("/healthz", (HttpContext context) => Results.Ok(new
    { accountId = context.Items[selectedAccountKey] as string, testCreditsOnly = true, productionEngine = true }));
app.MapFrontierPaidRounds(context => context.Items[selectedAccountKey] as string);
await app.RunAsync();
