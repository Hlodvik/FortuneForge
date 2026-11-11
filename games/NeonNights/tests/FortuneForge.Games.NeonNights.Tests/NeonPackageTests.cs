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
    public void NeonWildOnlyPaysThroughARealPayingSymbol()
    {
        var definitions = new NeonDefinitionSource();
        var game = definitions.GetGame(NeonArcadeSlotDefinitions.GameId)!;
        var symbols = definitions.GetSymbolSet(game.Symbols.SymbolSetId)!;
        var paytable = definitions.GetPaytable(game.Math.PaytableId)!;
        var evaluator = new NeonCombinationEvaluator();
        var calculator = new NeonPayoutCalculator();

        SpinPayout Pay(params string[] firstRow)
        {
            var reels = firstRow.Select(symbol => (IReadOnlyList<string>)new[] { symbol, "FREE", "FREE", "FREE" }).ToArray();
            return calculator.Calculate(evaluator.Evaluate(reels, game, symbols), game, paytable, 100);
        }

        Assert.DoesNotContain(paytable.Rules, rule => rule.SymbolId == "ACE");
        Assert.Contains(definitions.GetPaytable(NeonArcadeSlotDefinitions.PreviousPaytableId)!.Rules,
            rule => rule.SymbolId == "ACE" && rule.MatchLength == 5 && rule.Multiplier == 18);
        Assert.Empty(Pay("ACE", "ACE", "ACE", "7", "FREE").Paylines);
        Assert.Empty(Pay("ACE", "ACE", "ACE", "ACE", "ACE").Paylines);

        var three = Assert.Single(Assert.Single(Pay("ACE", "ACE", "7", "FREE", "FREE").Paylines).Matches);
        Assert.Equal("7", three.Match.SymbolId);
        Assert.Equal(246, three.AmountPoints);
        Assert.Equal(2, three.Match.WildPositions.Count);

        var five = Assert.Single(Assert.Single(Pay("2", "ACE", "ACE", "ACE", "ACE").Paylines).Matches);
        Assert.Equal("2", five.Match.SymbolId);
        Assert.Equal(328, five.AmountPoints);
        Assert.Equal(4, five.Match.WildPositions.Count);
    }

    [Fact]
    public void BonusCentRoundingAndPaidFeatureTransitionsKeepTheirEstablishedRules()
    {
        PaylinePayout line = new(1, 50,
            [new(new(1, "3", 3, [new(0,0), new(1,0), new(2,0)], []), 1, 50)]);
        var bonus = NeonArcadeBonus.Apply(new(50, [line]), [["FREE"], ["6"]]);
        Assert.Equal(63, bonus.Payout.TotalPoints);
        Assert.Equal(63, bonus.Payout.Paylines.Sum(payline => payline.AmountPoints));
        Assert.True(NeonPlayPolicy.ChargesWager(false, null));
        Assert.True(NeonPlayPolicy.ChargesWager(true, "arcade-rows"));
        Assert.True(NeonPlayPolicy.ChargesWager(true, "car-rows"));
        Assert.False(NeonPlayPolicy.ChargesWager(true, null));
        var retrigger = NeonPlayPolicy.AdvanceAwardedPlays(6, true, "arcade-rows", 7, 150, bonus.Evidence);
        Assert.Equal(new NeonAwardedPlayState(13, 150, "arcade-rows"), retrigger);
        Assert.Equal(new NeonAwardedPlayState(0, null, null),
            NeonPlayPolicy.AdvanceAwardedPlays(0, true, null, 0, 150, null));
    }

    [Fact]
    public void DiscoGrantsTwelvePaidPlaysAndReplaysThePreviousTenPlayGrant()
    {
        var trigger = NeonArcadeBonus.Apply(new(0, []), [["POWER"]]);
        var remaining = NeonPlayPolicy.NextDiscoSpins(0, false, trigger.Evidence);
        Assert.Equal(12, remaining);
        Assert.Equal(0, trigger.FreeSpinsAwarded);
        Assert.True(NeonPlayPolicy.ChargesWager(false, null));
        var play = NeonArcadeBonus.Apply(new(0, []), [["DISCO_BALL"]]);
        for (var expected = 11; expected >= 0; expected--)
        {
            remaining = NeonPlayPolicy.NextDiscoSpins(remaining, true, play.Evidence);
            Assert.Equal(expected, remaining);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => NeonPlayPolicy.NextDiscoSpins(13, true, play.Evidence));

        var capturedTrigger = NeonArcadeBonus.Apply(new(0, []), [["POWER"]],
            engineVersion: NeonArcadeSlotDefinitions.PreviousCarEngineVersion);
        Assert.True(NeonArcadeSlotDefinitions.SupportsPackageReplay(NeonArcadeSlotDefinitions.PreviousCarEngineVersion));
        Assert.Equal(NeonArcadeSlotDefinitions.PreviousCarModelId, capturedTrigger.Evidence.ModelId);
        Assert.Equal(10, NeonPlayPolicy.NextDiscoSpins(0, false, capturedTrigger.Evidence));
        var capturedPlay = NeonArcadeBonus.Apply(new(0, []), [["DISCO_BALL"]],
            engineVersion: NeonArcadeSlotDefinitions.PreviousCarEngineVersion);
        Assert.Equal(9, NeonPlayPolicy.NextDiscoSpins(10, true, capturedPlay.Evidence));
        Assert.Throws<ArgumentOutOfRangeException>(() => NeonPlayPolicy.NextDiscoSpins(11, true, capturedPlay.Evidence));
    }

    [Fact]
    public void CarAwardsPaidExpandedPlaysAndRetriggersAtTheLockedWager()
    {
        var bonus = NeonArcadeBonus.Apply(new(0, []), [["6"]]);
        Assert.Equal(7, bonus.FreeSpinsAwarded);
        Assert.Equal("car-rows", NeonArcadeBonus.AwardedFeatureMode(bonus.Evidence));
        var captured = NeonArcadeBonus.Apply(new(0, []), [["6"], ["SEAL_RAND"]],
            engineVersion: NeonArcadeSlotDefinitions.PreviousCarEngineVersion);
        Assert.Equal("car-rows", NeonArcadeBonus.AwardedFeatureMode(captured.Evidence));
        Assert.Equal("car", Assert.Single(captured.Evidence.Activators).Kind);
        var state = NeonPlayPolicy.AdvanceAwardedPlays(0, false, null,
            bonus.FreeSpinsAwarded, 150, bonus.Evidence);
        Assert.Equal(new NeonAwardedPlayState(7, 150, "car-rows"), state);
        Assert.True(NeonPlayPolicy.IsFeatureMode(state.FeatureMode));
        Assert.True(NeonPlayPolicy.ChargesWager(true, state.FeatureMode));

        var stops = NeonArcadeSlotDefinitions.CreateReelSet().Reels.Select(reel => reel.IndexOf("6")).ToArray();
        var result = new NeonNightsEngine(new Draws(stops, 73)).Spin(150, "car", false,
            0, state.FeatureMode, 0, true);
        Assert.All(result.Reels, reel => Assert.Equal(7, reel.Count));
        Assert.Equal(26, result.EngineAudit.EffectiveGame.Paylines.Count);
        Assert.Equal(7, result.FreeSpinsAwarded);
        Assert.Equal(new NeonAwardedPlayState(13, 150, "car-rows"),
            NeonPlayPolicy.AdvanceAwardedPlays(6, true, state.FeatureMode,
                result.FreeSpinsAwarded, 150, result.NeonArcade));
        Assert.Empty(NeonOutcomeReplay.Check(result, result.Payout));
    }

    [Fact]
    public void CabinetIsAbsentFromCurrentRulesWhileCapturedBonusVersionsRetainTheirRoles()
    {
        Assert.DoesNotContain(NeonArcadeSlotDefinitions.CreateSymbolSet().Symbols, symbol => symbol.Id == "SEAL_RAND");
        Assert.All(NeonArcadeSlotDefinitions.CreateReelSet().Reels, reel =>
        {
            Assert.Equal(4096, reel.Count);
            Assert.DoesNotContain("SEAL_RAND", reel);
            Assert.Equal(2, reel.Count(symbol => symbol == "6"));
            Assert.Equal(1, reel.Count(symbol => symbol == "POWER"));
        });
        var removed = NeonArcadeBonus.Apply(new(0, []), [["SEAL_RAND"]]);
        Assert.Empty(removed.Evidence.Activators);
        Assert.Equal(0, removed.FreeSpinsAwarded);

        var oldCar = NeonArcadeBonus.Apply(new(0, []), [["6"]],
            engineVersion: NeonArcadeSlotDefinitions.PreviousClusterEngineVersion);
        Assert.Equal(7, oldCar.FreeSpinsAwarded);
        Assert.Null(NeonArcadeBonus.AwardedFeatureMode(oldCar.Evidence));
        var oldCabinet = NeonArcadeBonus.Apply(new(0, []), [["SEAL_RAND"]],
            engineVersion: NeonArcadeSlotDefinitions.PreviousClusterEngineVersion);
        Assert.Equal(7, oldCabinet.FreeSpinsAwarded);
        Assert.Equal("arcade-rows", NeonArcadeBonus.AwardedFeatureMode(oldCabinet.Evidence));
        Assert.True(NeonPlayPolicy.ChargesWager(true, "arcade-rows"));
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

        var discoStops = NeonDiscoDefinitions.CreateReelSet().Reels.Select(reel => reel.IndexOf("2")).ToArray();
        var disco = new NeonNightsEngine(new Draws(discoStops, 73)).Spin(150, "disco", false,
            0, null, 0, false, true, neonDiscoMultiplier: 2);
        Assert.NotNull(disco.NeonDiscoCascade);
        Assert.NotEmpty(disco.NeonDiscoCascade.Steps);
        Assert.Equal(2, disco.NeonDiscoCascade.Steps[0].Multiplier);
        Assert.Equal(2, disco.EngineAudit.NeonDiscoCascade!.StartingMultiplier);
        Assert.Equal(disco.Payout.TotalPoints, disco.NeonDiscoCascade.Steps.Sum(step => step.Payout.TotalPoints));
        Assert.Empty(disco.Payout.Paylines);
        Assert.Equal(NeonDiscoDefinitions.SymbolSetId, disco.SymbolSetId);
        Assert.Equal(NeonDiscoDefinitions.ReelSetId, disco.ReelSetId);
        Assert.Equal(NeonDiscoDefinitions.PaytableId, disco.PaytableId);
        Assert.All(disco.Reels.SelectMany(reel => reel), symbol => Assert.Contains(symbol, NeonDiscoDefinitions.SymbolIds));
        Assert.Empty(disco.NeonArcade.Activators);
        Assert.Equal(0, disco.FreeSpinsAwarded);
        Assert.Empty(NeonOutcomeReplay.Check(disco, disco.Payout));
        var wrongStartingTier = disco with
        {
            EngineAudit = disco.EngineAudit with
            {
                NeonDiscoCascade = disco.EngineAudit.NeonDiscoCascade with { StartingMultiplier = 1 }
            }
        };
        Assert.Contains(NeonOutcomeReplay.Check(wrongStartingTier, disco.Payout),
            finding => finding.Code == "slot-neon-disco-steps-mismatch");
        var changedSteps = disco.NeonDiscoCascade.Steps.ToArray();
        changedSteps[0] = changedSteps[0] with { Multiplier = 5 };
        Assert.Contains(NeonOutcomeReplay.Check(disco with
        {
            NeonDiscoCascade = disco.NeonDiscoCascade with { Steps = changedSteps }
        }, disco.Payout), finding => finding.Code == "slot-neon-disco-steps-mismatch");
        Assert.All(disco.NeonDiscoCascade.Steps, step =>
        {
            Assert.NotEmpty(step.Cleared);
            Assert.Equal(step.Payout.TotalPoints, step.Payout.Paylines.Sum(line => line.AmountPoints));
            Assert.All(step.Reels.SelectMany(reel => reel), symbol => Assert.Contains(symbol, NeonDiscoDefinitions.SymbolIds));
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
