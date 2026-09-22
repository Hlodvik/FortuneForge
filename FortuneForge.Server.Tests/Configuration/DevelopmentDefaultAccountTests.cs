using FortuneForge.Server.Accounts.Development;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Server.Tests.Configuration;

public sealed class DevelopmentDefaultAccountTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public void DefaultAccountCanOnlyActivateInDevelopment(string environmentName, bool expected)
    {
        var account = new DevelopmentDefaultAccount(
            new TestHostEnvironment(environmentName),
            Configuration(enabled: true));

        Assert.Equal(expected, account.Enabled);
    }

    [Fact]
    public void DisabledConfigurationKeepsDevelopmentAccountOff()
    {
        var account = new DevelopmentDefaultAccount(
            new TestHostEnvironment("Development"),
            Configuration(enabled: false));

        Assert.False(account.Enabled);
    }

    [Fact]
    public void UsesTheConfiguredLocalSignInCredential()
    {
        var account = new DevelopmentDefaultAccount(
            new TestHostEnvironment("Development"),
            Configuration(enabled: true));

        Assert.Equal("FortuneForgeLocal!", account.Password);
    }

    private static IConfiguration Configuration(bool enabled) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["LocalDevelopment:DefaultAccount:Enabled"] = enabled.ToString(),
            ["LocalDevelopment:DefaultAccount:UserId"] = "local-dev-player",
            ["LocalDevelopment:DefaultAccount:PlayerName"] = "Local Player",
            ["LocalDevelopment:DefaultAccount:Email"] = "local.player@fortuneforge.test",
            ["LocalDevelopment:DefaultAccount:Password"] = "FortuneForgeLocal!",
            ["LocalDevelopment:DefaultAccount:StartingSlotsCredits"] = "10000"
        }).Build();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "FortuneForge.Server.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
