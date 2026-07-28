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
        services.AddSingleton(_ => CreateFirestore(configuration));
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

    private static FirestoreDb CreateFirestore(IConfiguration configuration)
    {
        var projectId = configuration["GoogleCloud:ProjectId"]
            ?? throw new InvalidOperationException("GoogleCloud:ProjectId is required.");
        var databaseId = configuration["GoogleCloud:FirestoreDatabaseId"] ?? "(default)";

        return new FirestoreDbBuilder
        {
            ProjectId = projectId,
            DatabaseId = databaseId
        }.Build();
    }
}
