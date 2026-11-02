using FortuneForge.Server.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FortuneForge.Server.Tests.Configuration;

public sealed class FirestoreEmulatorConfigurationTests
{
    [Fact]
    public void LocalDemoProjectCanUseExplicitEmulatorEndpoint()
    {
        var configuration = Configuration("demo-fortuneforge-e2e");

        var database = AccountServicesConfiguration.CreateFirestore(
            configuration,
            "127.0.0.1:8787");

        Assert.Equal("demo-fortuneforge-e2e", database.ProjectId);
        Assert.Equal("(default)", database.DatabaseId);
    }

    [Theory]
    [InlineData("fortuneforgegame", "127.0.0.1:8787")]
    [InlineData("demo-fortuneforge-e2e", "firestore.googleapis.com:443")]
    [InlineData("demo-fortuneforge-e2e", "127.0.0.1:0")]
    [InlineData("demo-fortuneforge-e2e", "localhost")]
    [InlineData("demo-fortuneforge-e2e", "127.0.0.1")]
    [InlineData("demo-fortuneforge-e2e", "[::1]")]
    [InlineData("demo-fortuneforge-e2e", "user:password@localhost:8787")]
    [InlineData("demo-fortuneforge-e2e", "localhost:8787#fragment")]
    [InlineData("demo-fortuneforge-e2e", "localhost:8787?database=other")]
    [InlineData("demo-fortuneforge-e2e", "localhost:8787/private")]
    [InlineData("demo-fortuneforge-e2e", "http://localhost:8787")]
    [InlineData("demo-fortuneforge-e2e", "localhost:65536")]
    public void EmulatorEndpointRejectsCloudProjectsAndNonLocalTargets(
        string projectId,
        string endpoint)
    {
        Assert.Throws<InvalidOperationException>(() =>
            AccountServicesConfiguration.CreateFirestore(Configuration(projectId), endpoint));
    }

    private static IConfiguration Configuration(string projectId) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoogleCloud:ProjectId"] = projectId,
            ["GoogleCloud:FirestoreDatabaseId"] = "(default)"
        }).Build();
}
