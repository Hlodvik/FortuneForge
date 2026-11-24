using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Games;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class ArcaneOwnershipTests
{
    [Fact]
    public void CataloguedArcaneCannotBeResolvedThroughTheHostClassicEngine()
    {
        var random = new CountingRandom();
        var registry = new StandaloneSlotPackageRegistry(random);
        var host = new SpinService(new OptionsSlotsDefinitionProvider(), new CryptoReelGenerator(random),
            new CombinationEvaluator(), new PayoutCalculator(), random, registry);
        const string game = ArcaneArchivesEngine.GameId;
        var admission = new SlotSpinAdmission(12, 300, false, 0, false, 0, null, 0, null)
        { AdmissionId = Guid.NewGuid(), StartedAtUtc = DateTime.UtcNow };

        Assert.NotNull(StandaloneSlotPackageRegistry.GetGame(game));
        Assert.False(StandaloneSlotPackageRegistry.Owns(game));
        Assert.False(registry.TrySpin(game, 12, "native-arcane-account", false, 0, null, 0, false, out _));
        Assert.Throws<ArgumentException>(() => host.ValidateRequest(game, 12));
        Assert.Throws<ArgumentException>(() => host.Spin(game, 12, "native-arcane-account", false));
        Assert.Throws<ArgumentException>(() => host.SpinAdmitted(game, admission, "native-arcane-account"));
        Assert.Equal(0, random.Calls);
    }

    private sealed class CountingRandom : IRandomIndexSource
    {
        public int Calls { get; private set; }
        public int Next(int maximumExclusive) { Calls++; return 0; }
    }
}
