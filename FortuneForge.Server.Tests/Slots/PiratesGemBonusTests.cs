using FortuneForge.Server.Slots.Bonuses;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class PiratesGemBonusTests
{
    private static readonly Guid SpinId = Guid.Parse("1d74460e-8742-4bcc-9fa5-82066f92d0b1");

    [Fact]
    public void GemsBeforeTheSeventhGrowThePileWithoutAwarding()
    {
        var progress = PiratesGemBonus.Advance(SpinId, "sync", 0, 0, 6, 100);

        Assert.Equal(6, progress.Count);
        Assert.Equal(600, progress.WagerTotal);
        Assert.Empty(progress.Awards);
    }

    [Fact]
    public void TwentyFifthGemGuaranteesAwardAndLaterGemsStartANewPile()
    {
        var progress = PiratesGemBonus.Advance(SpinId, "rows", 24, 2_400, 2, 100);

        Assert.Equal(new PiratesGemAward(25, 2_500), Assert.Single(progress.Awards));
        Assert.Equal(1, progress.Count);
        Assert.Equal(100, progress.WagerTotal);
    }

    [Fact]
    public void SeventhGemHasVariableButReplayableOutcomes()
    {
        var awarded = 0;
        for (var index = 0; index < 500; index++)
        {
            var spinId = Guid.Parse($"00000000-0000-0000-0000-{index:x12}");
            var first = PiratesGemBonus.Advance(spinId, "paw", 6, 600, 1, 100);
            var replay = PiratesGemBonus.Advance(spinId, "paw", 6, 600, 1, 100);

            Assert.Equal(first.Count, replay.Count);
            Assert.Equal(first.WagerTotal, replay.WagerTotal);
            Assert.Equal(first.Awards, replay.Awards);
            if (first.Awards.Count > 0) awarded++;
        }

        Assert.InRange(awarded, 1, 499);
    }

    [Fact]
    public void CombinedFeatureModesAreUniqueAndCanonical()
    {
        var combined = PiratesGemBonus.AddFeatureMode("rows-sync", "paw");
        combined = PiratesGemBonus.AddFeatureMode(combined, "rows");

        Assert.Equal("sync-rows-paw", combined);
    }
}
