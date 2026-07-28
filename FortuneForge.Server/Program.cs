using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Payments;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 32 * 1024;
});

if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var cloudRunPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{cloudRunPort}");
}

builder.AddServiceDefaults();

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAntiAbuseRateLimiting();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSingleton(_ =>
{
    var projectId = builder.Configuration["GoogleCloud:ProjectId"]
        ?? throw new InvalidOperationException("GoogleCloud:ProjectId is required.");
    var databaseId = builder.Configuration["GoogleCloud:FirestoreDatabaseId"] ?? "(default)";

    return new FirestoreDbBuilder
    {
        ProjectId = projectId,
        DatabaseId = databaseId
    }.Build();
});
builder.Services.AddSingleton<IPasswordHashingService, Pbkdf2PasswordHashingService>();
builder.Services.AddSingleton(_ => FirebaseApp.Create(new AppOptions
{
    ProjectId = builder.Configuration["GoogleCloud:ProjectId"],
    Credential = GoogleCredential.GetApplicationDefault()
}));
builder.Services.AddSingleton(serviceProvider =>
    FirebaseAuth.GetAuth(serviceProvider.GetRequiredService<FirebaseApp>()));
builder.Services.AddHttpClient<FirebaseEmailVerificationService>();
builder.Services.AddSingleton<IAccountStore, FirestoreAccountStore>();
builder.Services.AddHostedService<AccountSchemaInitializer>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddPayments(builder.Configuration);
builder.Services.AddOptions<SlotsOptions>()
    .Bind(builder.Configuration.GetSection(SlotsOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SlotsOptions>, SlotsOptionsValidator>();
builder.Services.AddSingleton<ISlotsDefinitionProvider, OptionsSlotsDefinitionProvider>();
builder.Services.AddSingleton<IRandomIndexSource, CryptoRandomIndexSource>();
builder.Services.AddSingleton<IReelGenerator, CryptoReelGenerator>();
builder.Services.AddSingleton<ICombinationEvaluator, CombinationEvaluator>();
builder.Services.AddSingleton<IPayoutCalculator, PayoutCalculator>();
builder.Services.AddSingleton<SpinService>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapStaticAssets();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();
}

app.UseRouting();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.XContentTypeOptions = "nosniff";
    }

    await next();
});
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

app.Run();
