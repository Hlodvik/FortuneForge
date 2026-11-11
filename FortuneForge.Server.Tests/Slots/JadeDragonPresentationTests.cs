using System.Text.Json;
using FortuneForge.Server.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class JadeDragonPresentationTests
{
    [Theory]
    [InlineData(true, "demo-jade-test", "127.0.0.1:8080", true)]
    [InlineData(true, "demo-jade-test", "localhost:8080", true)]
    [InlineData(false, "demo-jade-test", "127.0.0.1:8080", false)]
    [InlineData(true, "production-project", "127.0.0.1:8080", false)]
    [InlineData(true, "demo-jade-test", "example.com:8080", false)]
    [InlineData(true, "demo-jade-test", "", false)]
    public void Emulator_credentials_require_development_demo_project_and_loopback(
        bool development, string project, string host, bool expected)
    {
        Assert.Equal(expected, FortuneForge.Server.Configuration.AccountServicesConfiguration
            .CanUseEmulatorCredential(development, project, host));
    }

    [Fact]
    public void Paid_cap_is_server_enforced_but_historical_bonus_wagers_are_preserved()
    {
        FortuneForge.Server.Slots.Games.JadeDragon.JadeDragonWagerPolicy.Validate("empty-slots-v1", 400, false);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FortuneForge.Server.Slots.Games.JadeDragon.JadeDragonWagerPolicy.Validate("empty-slots-v1", 402, false));
        FortuneForge.Server.Slots.Games.JadeDragon.JadeDragonWagerPolicy.Validate("empty-slots-v1", 2000, true);
        FortuneForge.Server.Slots.Games.JadeDragon.JadeDragonWagerPolicy.Validate("another-game", 2000, false);
    }

    [Fact]
    public void Presentation_exposes_native_rules_without_reel_strips_or_wallet_state()
    {
        var response = Assert.IsType<OkObjectResult>(new JadeDragonPresentationController().Get());
        var json = JsonSerializer.Serialize(response.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("empty-slots-v1", root.GetProperty("gameId").GetString());
        Assert.Equal(23, root.GetProperty("symbols").GetArrayLength());
        Assert.Equal(4, root.GetProperty("layout").GetProperty("visibleRows").GetInt32());
        Assert.Equal(10000, root.GetProperty("maximumPresentationBetCents").GetInt32());
        Assert.False(root.TryGetProperty("reelSet", out _));
        Assert.False(root.TryGetProperty("balance", out _));
    }
}
