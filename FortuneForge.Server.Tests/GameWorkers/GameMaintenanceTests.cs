using System.Text.Json;
using FortuneForge.Server.Configuration;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

public sealed class GameMaintenanceTests
{
    [Fact]
    public void Committed_maintenance_policy_closes_every_listed_packages_admission_without_changing_other_capabilities()
    {
        using var stream = typeof(FortuneForgePipeline).Assembly.GetManifestResourceStream("FortuneForge.GameMaintenance.json")!;
        using var policy = JsonDocument.Parse(stream);
        using var packages = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "backend-host.json")));
        foreach (var entry in policy.RootElement.GetProperty("runtimeGameIds").EnumerateArray())
        {
            var id = entry.GetString()!;
            Assert.True(GameMaintenance.Contains(id));
            var settings = packages.RootElement.GetProperty("packages").GetProperty(id).EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.GetString());
            var before = new Dictionary<string, string?>(settings);
            GameMaintenance.ApplyAdmissionSettings(id, settings);
            Assert.Contains(settings, pair => pair.Key.EndsWith(":AllowNewPaidRounds", StringComparison.Ordinal) && pair.Value == "false");
            foreach (var pair in before.Where(pair => !pair.Key.EndsWith(":AllowNewPaidRounds", StringComparison.Ordinal)))
                Assert.Equal(pair.Value, settings[pair.Key]);
        }
    }

    [Theory]
    [InlineData("pirates-fortune-v1")]
    [InlineData("empty-slots-v1")]
    public void Active_games_keep_their_existing_admission_policy(string id)
    {
        Assert.False(GameMaintenance.Contains(id));
        var settings = new Dictionary<string, string?> { ["Test:AllowNewPaidRounds"] = "true" };
        GameMaintenance.ApplyAdmissionSettings(id, settings);
        Assert.Equal("true", settings["Test:AllowNewPaidRounds"]);
    }

    [Fact]
    public void A_maintenance_game_without_an_admission_switch_fails_closed() =>
        Assert.Throws<InvalidOperationException>(() => GameMaintenance.ApplyAdmissionSettings("rainbow-realm-fruits-v1", new Dictionary<string, string?>()));
}
