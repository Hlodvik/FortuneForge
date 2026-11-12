using Google.Api.Gax;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.PackagedGame.Server;

internal static class GameWorkerFirestore
{
    internal static FirestoreDb CreateHostedDatabase(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(configuration["GameWorker:DemoUserId"]))
            throw new InvalidOperationException("Hosted game workers require a verified account on each request.");

        var projectId = configuration["GoogleCloud:ProjectId"];
        var emulatorHost = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
        if (environment.IsDevelopment() && configuration.GetValue<bool>("GameWorker:AllowDevelopmentLoopback"))
            return DemoGameAccount.CreateEmulatorDatabase(configuration, projectId, emulatorHost);
        if (!string.IsNullOrWhiteSpace(emulatorHost))
            throw new InvalidOperationException("A hosted game worker may use an emulator only during explicit local verification.");

        if (string.IsNullOrWhiteSpace(projectId) || projectId.StartsWith("demo-", StringComparison.Ordinal))
            throw new InvalidOperationException("Hosted game workers require a non-emulator GoogleCloud:ProjectId.");

        return new FirestoreDbBuilder
        {
            ProjectId = projectId,
            DatabaseId = configuration["GoogleCloud:FirestoreDatabaseId"] ?? "(default)",
            EmulatorDetection = EmulatorDetection.None
        }.Build();
    }
}
