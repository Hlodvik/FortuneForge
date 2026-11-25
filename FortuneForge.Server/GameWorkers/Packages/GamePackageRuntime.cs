using System.Collections.Concurrent;
using System.Security.Cryptography;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Hosting.InProcess;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace FortuneForge.Server.GameWorkers.Packages;

internal sealed class GamePackageRuntime : IHostedLifecycleService, IGamePackageTransport
{
    private readonly GamePackageComposition composition;
    private readonly string directory;
    private readonly IConfiguration configuration;
    private readonly IHostEnvironment environment;
    private readonly ILogger<GamePackageRuntime> logger;
    private readonly ConcurrentDictionary<string, InProcessHttpEndpoint> endpoints = new(StringComparer.Ordinal);
    private readonly List<(WebApplication App, GamePackageLoadContext Context)> applications = [];
    private readonly Dictionary<string, string> settings;
    private readonly Dictionary<string, Dictionary<string, string>> packageSettings;
    internal InProcessHttpEndpoint Platform { get; } = new();
    internal bool Ready { get; private set; }

    internal GamePackageRuntime(GamePackageComposition composition, string directory, IConfiguration configuration,
        IHostEnvironment environment, Dictionary<string, string> hostSettings,
        Dictionary<string, Dictionary<string, string>> packageSettings, ILogger<GamePackageRuntime> logger)
    {
        this.composition = composition;
        this.directory = directory;
        this.configuration = configuration;
        this.environment = environment;
        this.packageSettings = packageSettings;
        this.logger = logger;
        settings = hostSettings;
    }

    public bool Contains(string gameId) => composition.Packages.Any(package => package.RuntimeGameId == gameId);

    public Task<HttpResponseMessage> SendAsync(string gameId, HttpRequestMessage request, CancellationToken token) =>
        Ready && endpoints.TryGetValue(gameId, out var endpoint)
            ? endpoint.SendAsync(request, token)
            : throw new GameWorkerUnavailableException(gameId);

    public Task StartingAsync(CancellationToken token) => Task.CompletedTask;
    public Task StartAsync(CancellationToken token) => Task.CompletedTask;

    public async Task StartedAsync(CancellationToken token)
    {
        try
        {
            foreach (var package in composition.Packages)
            {
                token.ThrowIfCancellationRequested();
                var root = GamePackageComposition.PackageDirectory(directory, package);
                var entry = GamePackageComposition.SafeFile(root, package.Entry);
                var context = new GamePackageLoadContext(entry);
                WebApplication? app = null;
                try
                {
                    using var reflection = context.EnterContextualReflection();
                    var assembly = context.LoadFromAssemblyPath(entry);
                    var type = assembly.GetType(package.ModuleType, throwOnError: true)!;
                    if (Activator.CreateInstance(type) is not IGameBackendPackage module || module.GameId != package.RuntimeGameId)
                        throw new InvalidOperationException("Backend package does not implement its selected game contract.");
                    var server = new InProcessGameServer();
                    app = module.Create(new WebApplicationOptions
                    {
                        ApplicationName = assembly.GetName().Name,
                        ContentRootPath = root,
                        EnvironmentName = environment.EnvironmentName,
                        Args = []
                    }, builder =>
                    {
                        // Child configuration contains its own capability keys only. Platform
                        // secrets and other games' keys never enter the child provider.
                        builder.Configuration.Sources.Clear();
                        var childSettings = CreatePackageSettings(package.RuntimeGameId);
                        childSettings[HostDefaults.ApplicationKey] = assembly.GetName().Name;
                        childSettings[HostDefaults.EnvironmentKey] = environment.EnvironmentName;
                        childSettings[HostDefaults.ContentRootKey] = root;
                        builder.Configuration.AddInMemoryCollection(childSettings);
                        builder.Logging.ClearProviders();
                        builder.Logging.AddConsole();
                        builder.Services.RemoveAll<IServer>();
                        builder.Services.AddSingleton<IServer>(server);
                        builder.Services.PostConfigureAll<HttpClientFactoryOptions>(options =>
                            options.HttpMessageHandlerBuilderActions.Add(handler =>
                                handler.PrimaryHandler = new PlatformMessageHandler(Platform)));
                    });
                    applications.Add((app, context));
                    endpoints[package.RuntimeGameId] = server.Endpoint;
                    await app.StartAsync(token);
                    using var health = new HttpRequestMessage(HttpMethod.Get, "https://game-package.internal/healthz");
                    health.Headers.Add(GameWorkerAuthentication.AssertionHeader,
                        GameWorkerAuthentication.CreateAssertion(package.RuntimeGameId, "package-health", "GET", "/healthz", [],
                            GameWorkerAuthentication.ReadSigningKey(settings[$"GameWorkers:{package.RuntimeGameId}:SigningKey"]),
                            DateTimeOffset.UtcNow));
                    using var response = await server.Endpoint.SendAsync(health, token);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException($"Backend package health failed: {package.GameId}.");
                    logger.LogInformation("Loaded backend package {GameId} release {ReleaseId}", package.GameId, package.ReleaseId);
                }
                catch
                {
                    if (app is null) context.Unload();
                    throw;
                }
            }
            Ready = true;
        }
        catch
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await StopApplicationsAsync(cleanup.Token); }
            catch (Exception error) { logger.LogError(error, "Backend package startup cleanup failed."); }
            throw;
        }
    }

    private Dictionary<string, string?> CreatePackageSettings(string gameId)
    {
        var result = packageSettings.TryGetValue(gameId, out var configured)
            ? configured.ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in result.Keys.ToArray())
            result[key] = configuration[$"GamePackages:Games:{gameId}:{key}"] ?? configuration[key] ?? result[key];
        FortuneForge.Server.Configuration.GameMaintenance.ApplyAdmissionSettings(gameId, result);
        result["GoogleCloud:ProjectId"] = configuration["GoogleCloud:ProjectId"];
        result["GoogleCloud:FirestoreDatabaseId"] = configuration["GoogleCloud:FirestoreDatabaseId"] ?? "(default)";
        result["GameWorker:Mode"] = "hosted";
        result["GameWorker:SigningKey"] = settings[$"GameWorkers:{gameId}:SigningKey"];
        result["GameSecurityRequests:Enabled"] = configuration["GameSecurityRequests:Enabled"] ?? "true";
        result["Logging:LogLevel:Default"] = configuration["Logging:LogLevel:Default"] ?? "Information";
        result["Logging:LogLevel:Microsoft.AspNetCore"] = configuration["Logging:LogLevel:Microsoft.AspNetCore"] ?? "Warning";
        result["Logging:LogLevel:Microsoft.Hosting.Lifetime"] = "Warning";
        result["GameWallet:Url"] = "https://platform.game-packages.internal";
        result["GameWallet:SigningKey"] = settings[$"GameWallet:Games:{gameId}:SigningKey"];
        result["GameWallet:BatchSigningKey"] = settings[$"GameWallet:Games:{gameId}:BatchSigningKey"];
        result["ManagedPlayerRoster:Url"] = "https://platform.game-packages.internal";
        result["ManagedPlayerRoster:SigningKey"] = settings[$"ManagedPlayerRoster:Games:{gameId}:SigningKey"];
        if (environment.IsDevelopment() && configuration.GetValue<bool>("GamePackages:AllowDevelopmentEmulator"))
            result["GameWorker:AllowDevelopmentLoopback"] = "true";
        return result;
    }

    public Task StoppingAsync(CancellationToken token) { Ready = false; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken token) => StopApplicationsAsync(token);
    public Task StoppedAsync(CancellationToken token) => Task.CompletedTask;

    private async Task StopApplicationsAsync(CancellationToken token)
    {
        Ready = false;
        endpoints.Clear();
        while (applications.Count > 0)
        {
            var item = applications[^1];
            applications.RemoveAt(applications.Count - 1);
            try { await item.App.StopAsync(token); }
            catch (Exception error) { logger.LogError(error, "A backend package could not stop cleanly."); }
            try { await item.App.DisposeAsync(); }
            catch (Exception error) { logger.LogError(error, "A backend package could not be disposed cleanly."); }
            finally { item.Context.Unload(); }
        }
        await Platform.StopAsync(token);
    }

    internal static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private sealed class PlatformMessageHandler(InProcessHttpEndpoint endpoint) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri is not { IsAbsoluteUri: true } uri ||
                uri.Scheme != "https" || uri.Authority != "platform.game-packages.internal" ||
                !uri.AbsolutePath.StartsWith("/internal/", StringComparison.Ordinal))
                throw new HttpRequestException("A game package attempted an unsupported platform connection.");
            return endpoint.SendAsync(request, token);
        }
    }
}
