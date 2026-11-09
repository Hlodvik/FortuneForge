using FortuneForge.Games.NeonNights;
using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.NeonNights.Tests;

public sealed class NeonPackageTests
{
    [Fact]
    public void PackageOwnsDefinitionsWithoutAnApplicationOrAnotherGame()
    {
        var definitions = new NeonDefinitionSource();
        var game = definitions.GetGame(NeonArcadeSlotDefinitions.GameId)!;
        Assert.Null(definitions.GetGame("dragon-hoard-v1"));
        Assert.Equal(23, game.Paylines.Count);
        Assert.Null(game.Energy);
        Assert.Null(game.FreeGames);
        Assert.Equal(0.82m, game.Math.PayoutMultiplier);
        Assert.All(definitions.GetReelSet(game.Math.ReelSetId)!.Reels,
            reel => Assert.Equal(4096, reel.Count));
        Assert.DoesNotContain(typeof(NeonNightsEngine).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name!.StartsWith("FortuneForge.Server", StringComparison.Ordinal));
        var engine = new NeonNightsEngine(new Draws([], 73));
        engine.ValidateRequest(150);
        engine.ValidateRequest(75, usesAwardedPlay: true);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.ValidateRequest(75));
    }

    [Fact]
    public void BonusCentRoundingAndPaidFeatureTransitionsKeepTheirEstablishedRules()
    {
        PaylinePayout line = new(1, 50,
            [new(new(1, "3", 3, [new(0,0), new(1,0), new(2,0)], []), 1, 50)]);
        var bonus = NeonArcadeBonus.Apply(new(50, [line]), [["FREE"], ["SEAL_RAND"]]);
        Assert.Equal(63, bonus.Payout.TotalPoints);
        Assert.Equal(63, bonus.Payout.Paylines.Sum(payline => payline.AmountPoints));
        Assert.True(NeonPlayPolicy.ChargesWager(false, null));
        Assert.True(NeonPlayPolicy.ChargesWager(true, "arcade-rows"));
        Assert.False(NeonPlayPolicy.ChargesWager(true, null));
        var retrigger = NeonPlayPolicy.AdvanceAwardedPlays(6, true, "arcade-rows", 7, 150, bonus.Evidence);
        Assert.Equal(new NeonAwardedPlayState(13, 150, "arcade-rows"), retrigger);
        Assert.Equal(new NeonAwardedPlayState(0, null, null),
            NeonPlayPolicy.AdvanceAwardedPlays(0, true, null, 0, 150, null));
        Assert.Equal(9, NeonPlayPolicy.NextDiscoSpins(10, true, null));
    }

    [Fact]
    public void ArcadeGridAndDiscoCascadesExecuteUsingOnlyTheGamePackage()
    {
        var definitions = new NeonDefinitionSource();
        var game = definitions.GetGame(NeonArcadeSlotDefinitions.GameId)!;
        var reels = definitions.GetReelSet(game.Math.ReelSetId)!.Reels;
        var stops = reels.Select(reel => reel.IndexOf("3")).ToArray();
        var arcade = new NeonNightsEngine(new Draws(stops, 73)).Spin(150, "arcade", false,
            0, "arcade-rows", 0, true);
        Assert.All(arcade.Reels, reel => Assert.Equal(7, reel.Count));
        Assert.Equal(26, arcade.EngineAudit.EffectiveGame.Paylines.Count);
        Assert.Equal(arcade.Payout.TotalPoints, arcade.Payout.Paylines.Sum(line => line.AmountPoints));
        Assert.Empty(NeonOutcomeReplay.Check(arcade, arcade.Payout));
        Assert.NotEmpty(NeonOutcomeReplay.Check(arcade, arcade.Payout with { TotalPoints = arcade.Payout.TotalPoints + 1 }));

        var disco = new NeonNightsEngine(new Draws(stops, 73)).Spin(150, "disco", false,
            0, null, 0, false, true);
        Assert.NotNull(disco.NeonDiscoCascade);
        Assert.NotEmpty(disco.NeonDiscoCascade.Steps);
        Assert.Equal(disco.Payout.TotalPoints, disco.NeonDiscoCascade.Steps.Sum(step => step.Payout.TotalPoints));
        Assert.Empty(disco.Payout.Paylines);
        Assert.Empty(NeonOutcomeReplay.Check(disco, disco.Payout));
        Assert.All(disco.NeonDiscoCascade.Steps, step =>
        {
            Assert.NotEmpty(step.Cleared);
            Assert.Equal(step.Payout.TotalPoints, step.Payout.Paylines.Sum(line => line.AmountPoints));
        });
        Assert.Throws<ArgumentException>(() => new NeonNightsEngine(new Draws(stops, 73))
            .Spin(150, "invalid", false, 0, "arcade-rows", 0, true, true));
    }

    private sealed class Draws(int[] stops, int seed) : IGameRandomSource
    {
        private int cursor;
        private readonly Random random = new(seed);
        public int Next(int maximumExclusive) => cursor < stops.Length
            ? stops[cursor++] : random.Next(maximumExclusive);
    }
}
