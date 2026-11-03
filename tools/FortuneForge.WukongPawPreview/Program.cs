using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.Extensions.Options;

// Deliberately a separate loopback host. Do not register the production app,
// authentication, Firestore, accounts, wallet or /api/slots endpoints here.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:4197");
var allowedOrigins = new[] { "http://127.0.0.1:4196", "http://localhost:4196", "http://127.0.0.1:4197" };
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(allowedOrigins).AllowAnyHeader().WithMethods("GET", "POST").AllowCredentials()));
var app = builder.Build();
app.UseCors();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) ||
        context.Request.Headers.Origin.Any(origin => origin is null || !allowedOrigins.Contains(origin, StringComparer.Ordinal)))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "This synthetic fixture is loopback-only with an explicit local preview origin." });
        return;
    }
    await next();
});

var repository = FindRepository();
using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository, "FortuneForge.Server", "appsettings.json")));
var configuredSlots = document.RootElement.GetProperty("Slots").Deserialize<SlotsOptions>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
    ?? throw new InvalidOperationException("The real slot definitions are missing.");
var definitions = new OptionsSlotsDefinitionProvider(Options.Create(configuredSlots));
var game = definitions.GetGame("wukong-journey-v1") ?? throw new InvalidOperationException("Wukong is not configured.");
var random = new CryptoRandomIndexSource();
var engine = new SpinService(definitions, new CryptoReelGenerator(random), new CombinationEvaluator(), new PayoutCalculator(), random);
var sessions = new ConcurrentDictionary<string, WukongPawPrototypeSession>(StringComparer.Ordinal);
const string cookieName = "wukong-paw-synthetic-owner";
const string prefix = "/__prototype/wukong-paws";

(string Owner, WukongPawPrototypeSession Session) Current(HttpContext context)
{
    if (context.Request.Cookies.TryGetValue(cookieName, out var existing) && sessions.TryGetValue(existing, out var session))
        return (existing, session);
    var owner = Guid.NewGuid().ToString("N");
    var created = new WukongPawPrototypeSession(owner, game, engine);
    sessions[owner] = created;
    context.Response.Cookies.Append(cookieName, owner, new CookieOptions
    {
        HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true, Path = prefix
    });
    return (owner, created);
}
IResult Execute(HttpContext context, Func<WukongPawPrototypeSession, string, WukongPawPrototypeState> action)
{
    var (owner, session) = Current(context);
    try { return Results.Json(action(session, owner)); }
    catch (WukongPawPrototypeException exception)
    {
        return Results.Json(session.Get(owner) with { Error = exception.Message }, statusCode: exception.StatusCode);
    }
}
app.MapGet(prefix, (HttpContext context) => Execute(context, (session, owner) => session.Get(owner)));
app.MapGet(prefix + "/state", (HttpContext context) => Execute(context, (session, owner) => session.Get(owner)));
app.MapPost(prefix + "/place", (HttpContext context, WukongPawPrototypePlaceRequest request) => Execute(context, (session, owner) => session.Place(owner, request)));
app.MapPost(prefix + "/clear", (HttpContext context, WukongPawPrototypeClearRequest request) => Execute(context, (session, owner) => session.Clear(owner, request)));
app.MapPost(prefix + "/spin", (HttpContext context, WukongPawPrototypeSpinRequest request) => Execute(context, (session, owner) => session.Spin(owner, request)));
app.MapPost(prefix + "/next-preview", (HttpContext context, WukongPawPrototypeFixtureRequest request) => Execute(context, (session, owner) => session.NextPreview(owner, request)));
app.MapPost(prefix + "/reset-fixture", (HttpContext context, WukongPawPrototypeFixtureRequest request) => Execute(context, (session, owner) => session.ResetFixture(owner, request)));
app.MapPost(prefix + "/fixture-earn-symbol", (HttpContext context, WukongPawPrototypeEarnFixtureRequest request) => Execute(context, (session, owner) => session.SetEarnFixture(owner, request)));
app.Run();

static string FindRepository()
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "FortuneForge.Server", "appsettings.json"))) return directory.FullName;
    throw new InvalidOperationException("Run this local prototype from the existing repository.");
}
