using FirebaseAdmin;
using FirebaseAdmin.Auth;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Development;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Configuration;

public static class AccountServicesConfiguration
{
    public static IServiceCollection AddAccountServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddAntiAbuseRateLimiting();
        services.AddSingleton(_ => CreateFirestore(
            configuration,
            Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")));
        services.AddSingleton<IPasswordHashingService, Pbkdf2PasswordHashingService>();
        services.AddSingleton(provider => FirebaseApp.Create(new AppOptions
        {
            ProjectId = configuration["GoogleCloud:ProjectId"],
            // Local demo requests still construct FirebaseAuth through AccountService's DI graph.
            // Avoid requiring production ADC for an explicitly validated localhost emulator.
            Credential = CanUseEmulatorCredential(provider.GetRequiredService<IHostEnvironment>().IsDevelopment(),
                configuration["GoogleCloud:ProjectId"], Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST"))
                    ? GoogleCredential.FromAccessToken("owner")
                    : GoogleCredential.GetApplicationDefault()
        }));
        services.AddSingleton(provider =>
            FirebaseAuth.GetAuth(provider.GetRequiredService<FirebaseApp>()));
        services.AddHttpClient<FirebaseEmailVerificationService>();
        services.AddSingleton<IAccountStore, FirestoreAccountStore>();
        services.AddHostedService<AccountSchemaInitializer>();
        services.AddSingleton<DevelopmentDefaultAccount>();
        services.AddHostedService<DevelopmentDefaultAccountBootstrapper>();
        services.AddSingleton<AccountService>();

        return services;
    }

    internal static bool CanUseEmulatorCredential(bool development, string? projectId, string? emulatorHost) =>
        development && projectId?.StartsWith("demo-", StringComparison.Ordinal) == true &&
        TryValidateLocalEmulatorHost(emulatorHost ?? "", out _);

    internal static FirestoreDb CreateFirestore(
        IConfiguration configuration,
        string? emulatorHost = null)
    {
        var projectId = configuration["GoogleCloud:ProjectId"]
            ?? throw new InvalidOperationException("GoogleCloud:ProjectId is required.");
        var databaseId = configuration["GoogleCloud:FirestoreDatabaseId"] ?? "(default)";

        if (!string.IsNullOrWhiteSpace(emulatorHost))
        {
            if (!projectId.StartsWith("demo-", StringComparison.Ordinal) ||
                !TryValidateLocalEmulatorHost(emulatorHost, out var endpoint))
            {
                throw new InvalidOperationException(
                    "FIRESTORE_EMULATOR_HOST is accepted only for a localhost demo-* project.");
            }

            return new FirestoreDbBuilder
            {
                ProjectId = projectId,
                DatabaseId = databaseId,
                Endpoint = endpoint,
                ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
                EmulatorDetection = Google.Api.Gax.EmulatorDetection.None,
                // Explicit endpoints bypass the SDK's automatic emulator credential setup.
                // The emulator recognizes this owner token as the server/IAM path; browser
                // credentials must still pass security rules. This branch is restricted above
                // to an explicit loopback endpoint and a non-production demo-* project.
                Settings = new Google.Cloud.Firestore.V1.FirestoreSettings
                {
                    CallSettings = Google.Api.Gax.Grpc.CallSettings.FromHeader("authorization", "Bearer owner")
                }
            }.Build();
        }

        return new FirestoreDbBuilder
        {
            ProjectId = projectId,
            DatabaseId = databaseId
        }.Build();
    }

    private static bool TryValidateLocalEmulatorHost(string value, out string endpoint)
    {
        endpoint = value.Trim();
        var portSeparator = endpoint.LastIndexOf(':');
        if (!Uri.TryCreate($"http://{endpoint}", UriKind.Absolute, out var uri) ||
            !uri.IsLoopback ||
            portSeparator < 1 || !int.TryParse(endpoint.AsSpan(portSeparator + 1), out var explicitPort) ||
            explicitPort != uri.Port ||
            uri.Port is < 1 or > 65535 ||
            uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            endpoint = string.Empty;
            return false;
        }

        return true;
    }
}
