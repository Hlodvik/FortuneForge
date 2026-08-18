using FirebaseAdmin;
using FirebaseAdmin.Auth;
using FortuneForge.Server.Accounts;
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
        services.AddSingleton(_ => FirebaseApp.Create(new AppOptions
        {
            ProjectId = configuration["GoogleCloud:ProjectId"],
            Credential = GoogleCredential.GetApplicationDefault()
        }));
        services.AddSingleton(provider =>
            FirebaseAuth.GetAuth(provider.GetRequiredService<FirebaseApp>()));
        services.AddHttpClient<FirebaseEmailVerificationService>();
        services.AddSingleton<IAccountStore, FirestoreAccountStore>();
        services.AddHostedService<AccountSchemaInitializer>();
        services.AddSingleton<AccountService>();

        return services;
    }

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
                EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
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
        if (!Uri.TryCreate($"http://{endpoint}", UriKind.Absolute, out var uri) ||
            !uri.IsLoopback ||
            uri.Port is < 1 or > 65535 ||
            !string.IsNullOrEmpty(uri.PathAndQuery.Trim('/')))
        {
            endpoint = string.Empty;
            return false;
        }

        return true;
    }
}
