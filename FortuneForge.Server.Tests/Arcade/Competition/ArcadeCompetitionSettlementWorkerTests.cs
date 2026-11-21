using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Server.Arcade.Competition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

public sealed class ArcadeCompetitionSettlementWorkerTests
{
    [Fact]
    public void PlatformRegistersReadAndCreditServicesWithoutAScheduler()
    {
        var services = new ServiceCollection();
        services.AddArcadeCompetitions(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IArcadeCompetitionStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ArcadeCompetitionService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IArcadeCompetitionPayoutCreditor));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ArcadeCompetitionSettlementPlanningService));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IArcadeCompetitionSettlementExecutor));
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void GameWorkerComposesItsOwnSettlementScheduler()
    {
        var services = new ServiceCollection();
        services.AddArcadeCompetitionSettlementScheduler<FortuneForge.Games.Asteroids.Server.AsteroidsPaidRun>("asteroids");

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ArcadeCompetitionSettlementPlanningService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IArcadeCompetitionSettlementExecutor));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IHostedService));
    }
}
