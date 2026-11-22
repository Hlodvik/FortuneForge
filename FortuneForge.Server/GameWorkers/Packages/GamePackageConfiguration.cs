using System.Text.Json;
using System.Security.Cryptography;

namespace FortuneForge.Server.GameWorkers.Packages;

internal static class GamePackageConfiguration
{
    internal static void AddGamePackages(this WebApplicationBuilder builder)
    {
        var directory = builder.Configuration["GamePackages:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "game-packages");
        if (!File.Exists(Path.Combine(directory, "composition.json")))
        {
            if (builder.Configuration.GetValue<bool>("GamePackages:Required"))
                throw new InvalidOperationException("The shared backend requires its selected game packages.");
            return;
        }
        if (builder.Configuration["GamePackages:ManifestSha256"] is { Length: > 0 } expectedHash &&
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "composition.json")))) != expectedHash)
            throw new InvalidOperationException("The backend composition differs from the deployed package selection.");
        var composition = GamePackageComposition.ReadAndVerify(directory);
        var policies = JsonSerializer.Deserialize<PackagePolicies>(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "backend-host.json")), GamePackageComposition.Json)
            ?? throw new InvalidOperationException("Missing backend package policy.");
        var settings = policies.Host.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var ids = composition.Packages.Select(package => package.RuntimeGameId).ToHashSet(StringComparer.Ordinal);
        if (!ids.SetEquals(policies.Packages.Keys))
            throw new InvalidOperationException("Selected backend packages differ from the host capability policy.");
        foreach (var package in composition.Packages)
        {
            var id = package.RuntimeGameId;
            settings[$"GameWorkers:{id}:Url"] = $"https://{id}.game-packages.internal";
            settings[$"GameWorkers:{id}:SigningKey"] = GamePackageRuntime.NewKey();
            settings[$"GameWallet:Games:{id}:SigningKey"] = GamePackageRuntime.NewKey();
            settings[$"GameWallet:Games:{id}:BatchSigningKey"] = GamePackageRuntime.NewKey();
            settings[$"ManagedPlayerRoster:Games:{id}:SigningKey"] = GamePackageRuntime.NewKey();
        }
        builder.Configuration.AddInMemoryCollection(settings.Select(pair => KeyValuePair.Create(pair.Key, (string?)pair.Value)));
        builder.Services.AddSingleton(provider => new GamePackageRuntime(composition, directory,
            builder.Configuration, builder.Environment, settings, policies.Packages,
            provider.GetRequiredService<ILogger<GamePackageRuntime>>()));
        builder.Services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<GamePackageRuntime>());
        builder.Services.AddSingleton<IGamePackageTransport>(provider => provider.GetRequiredService<GamePackageRuntime>());
    }

    internal static void BindGamePackagePlatform(this WebApplication app)
    {
        if (app.Services.GetService<GamePackageRuntime>() is not { } packages) return;
        app.MapGet("/healthz", () => packages.Ready
            ? Results.Ok(new { status = "ready" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        app.Use(next =>
        {
            packages.Platform.Bind(next, app.Services);
            return next;
        });
    }

    private sealed record PackagePolicies(Dictionary<string, string> Host,
        Dictionary<string, Dictionary<string, string>> Packages);
}
