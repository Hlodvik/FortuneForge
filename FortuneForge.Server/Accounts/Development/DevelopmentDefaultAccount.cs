using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Development;

/// <summary>
/// Supplies one disposable authenticated player for the local Development host.
/// The instance can never activate outside Development, even if configuration is
/// accidentally copied to another environment.
/// </summary>
public sealed class DevelopmentDefaultAccount(IHostEnvironment environment, IConfiguration configuration)
{
    private const string ConfigurationPath = "LocalDevelopment:DefaultAccount";

    public bool Enabled { get; } = environment.IsDevelopment() &&
        configuration.GetValue<bool>($"{ConfigurationPath}:Enabled");

    public string UserId { get; } = configuration[$"{ConfigurationPath}:UserId"]
        ?? "local-dev-player";

    public string PlayerName { get; } = configuration[$"{ConfigurationPath}:PlayerName"]
        ?? "Local Player";

    public string Email { get; } = configuration[$"{ConfigurationPath}:Email"]
        ?? "local.player@fortuneforge.test";

    // This is a disposable, Development-only credential. It lets a developer
    // deliberately exercise the normal sign-in flow instead of depending on
    // an implicit session.
    public string Password { get; } = configuration[$"{ConfigurationPath}:Password"]
        ?? "FortuneForgeLocal!";

    public decimal StartingSlotsCredits { get; } = configuration.GetValue<decimal>(
        $"{ConfigurationPath}:StartingSlotsCredits",
        10_000m);

    // The token is process-local. It is never written to configuration or exposed
    // to the browser; local middleware attaches it only to otherwise-anonymous API requests.
    public string Token { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    public string TokenHash => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(Token)));
}

public sealed class DevelopmentDefaultAccountBootstrapper(
    DevelopmentDefaultAccount defaultAccount,
    IAccountStore accountStore,
    IPasswordHashingService passwordHashingService,
    FirestoreDb database,
    ILogger<DevelopmentDefaultAccountBootstrapper> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!defaultAccount.Enabled)
        {
            return;
        }

        var account = await accountStore.FindByIdAsync(defaultAccount.UserId, cancellationToken);
        if (account is null)
        {
            var created = await accountStore.CreateAsync(
                defaultAccount.UserId,
                defaultAccount.PlayerName,
                defaultAccount.PlayerName.ToUpperInvariant(),
                defaultAccount.Email,
                passwordHashingService.Hash(defaultAccount.Password),
                "active",
                cancellationToken);
            account = created.Value ?? throw new InvalidOperationException(
                $"The local default account could not be created: {created.Error}.");

            await SeedStartingCreditsAsync(cancellationToken);
        }
        else if (!passwordHashingService.Verify(defaultAccount.Password, account.PasswordHash))
        {
            // Emulators can preserve their data between server restarts. Keep
            // the documented local credential usable when that happens.
            await accountStore.UpdatePasswordHashAsync(
                defaultAccount.UserId,
                passwordHashingService.Hash(defaultAccount.Password),
                DateTime.UtcNow,
                cancellationToken);
        }

        await accountStore.CreateSessionAsync(
            defaultAccount.TokenHash,
            account.Account.UserId,
            DateTime.UtcNow,
            DateTime.UtcNow.AddDays(30),
            "127.0.0.1",
            cancellationToken);

        logger.LogInformation(
            "Local Development default account {PlayerName} is ready.",
            account.Account.PlayerName);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private Task SeedStartingCreditsAsync(CancellationToken cancellationToken)
    {
        var creditsInCents = checked((long)(defaultAccount.StartingSlotsCredits * 100m));
        var wholeRand = creditsInCents / 100;
        var fractionalCents = creditsInCents % 100;
        var balance = database.Collection("userBalances")
            .Document($"{defaultAccount.UserId}_slotsCredits");

        return balance.SetAsync(new Dictionary<string, object>
        {
            ["available"] = wholeRand,
            ["availableFractionalCents"] = fractionalCents,
            ["reserved"] = 0L,
            ["updatedAt"] = Timestamp.FromDateTime(DateTime.UtcNow)
        }, SetOptions.MergeAll, cancellationToken);
    }
}

public static class DevelopmentDefaultAccountPipeline
{
    public static WebApplication UseDevelopmentDefaultAccount(this WebApplication app)
    {
        var defaultAccount = app.Services.GetRequiredService<DevelopmentDefaultAccount>();
        if (!defaultAccount.Enabled)
        {
            return app;
        }

        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api") &&
                string.IsNullOrWhiteSpace(AccountSessionCookie.Read(context.Request)))
            {
                context.Request.Headers.Authorization = $"Bearer {defaultAccount.Token}";
            }

            await next();
        });

        return app;
    }
}
