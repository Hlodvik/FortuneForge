using System.Text.Json;
using System.Text.Json.Nodes;
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
        Assert.Equal(0.765m, game.Math.PayoutMultiplier);
        Assert.All(definitions.GetReelSet(game.Math.ReelSetId)!.Reels,
            reel => Assert.Equal(4096, reel.Count));
        Assert.DoesNotContain(typeof(NeonNightsEngine).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name!.StartsWith("FortuneForge.Server", StringComparison.Ordinal));
        var engine = new NeonNightsEngine(new Draws([], 73));
        engine.ValidateRequest(150);
        engine.ValidateRequest(10_000);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.ValidateRequest(10_050));
        engine.ValidateRequest(10_050, usesAwardedPlay: true);
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
        Assert.Equal(230, three.AmountPoints);
        Assert.Equal(2, three.Match.WildPositions.Count);

        var five = Assert.Single(Assert.Single(Pay("2", "ACE", "ACE", "ACE", "ACE").Paylines).Matches);
        Assert.Equal("2", five.Match.SymbolId);
        Assert.Equal(306, five.AmountPoints);
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
        Assert.Equal(56, result.EngineAudit.EffectiveGame.Paylines.Count);
        Assert.Equal(7, result.FreeSpinsAwarded);
        Assert.Equal(new NeonAwardedPlayState(13, 150, "car-rows"),
            NeonPlayPolicy.AdvanceAwardedPlays(6, true, state.FeatureMode,
                result.FreeSpinsAwarded, 150, result.NeonArcade));
        Assert.Empty(NeonOutcomeReplay.Check(result, result.Payout));
    }

    [Fact]
    public void CarPlaysPaysTheTwoLowerDiagonalsAfterNitroExpansion()
    {
        var result = SpinCarWithReels([
            ["3", "SEAL_PAW", "NITRO", "SEAL_PAW", "7", "SEAL_ROWS", "3"],
            ["2", "SEAL_ROWS", "NITRO", "SEAL_SYNC", "SEAL_PAW", "7", "4"],
            ["2", "5", "7", "4", "5", "4", "7"],
            ["SEAL_ROWS", "4", "ACE", "3", "SEAL_ROWS", "3", "SEAL_PAW"],
            ["SEAL_ROWS", "ACE", "SEAL_ROWS", "SEAL_ROWS", "3", "3", "2"]
        ]);

        var matches = result.Payout.Paylines.SelectMany(line => line.Matches).Select(paid => paid.Match).ToArray();
        Assert.Equal(2, matches.Length);
        Assert.Contains(matches, match => match.SymbolId == "SEAL_PAW" &&
            match.Positions.SequenceEqual([new(0, 3), new(1, 4), new(2, 5)]));
        Assert.Contains(matches, match => match.SymbolId == "7" &&
            match.Positions.SequenceEqual([new(0, 4), new(1, 5), new(2, 6)]));
        Assert.All(matches, match => Assert.Single(match.WildPositions));
        Assert.Empty(NeonOutcomeReplay.Check(result, result.Payout));

        var audit = result.EngineAudit;
        var oldLayout = JsonSerializer.SerializeToNode(audit.EffectiveGame)!;
        oldLayout["Layout"]!["PaylineCount"] = 26;
        oldLayout["Paylines"] = JsonSerializer.SerializeToNode(audit.EffectiveGame.Paylines.Take(26));
        oldLayout["Math"]!["PaylinePayoutSteps"] = JsonSerializer.SerializeToNode(audit.EffectiveGame.Math.PaylinePayoutSteps.Take(26));
        var oldGame = oldLayout.Deserialize<GameDefinition>()!;
        Assert.Contains(NeonOutcomeReplay.Check(result with { EngineAudit = audit with { EffectiveGame = oldGame } }, result.Payout),
            finding => finding.Code == "slot-neon-car-paylines-mismatch");

        var historicalVersion = NeonArcadeSlotDefinitions.PreviousNitroChoiceEngineVersion;
        var historicalBonus = NeonArcadeBonus.Apply(new(0, []), audit.RawGeneratedReels,
            isFreeSpin: true, engineVersion: historicalVersion);
        var historical = result with
        {
            EngineAudit = audit with
            {
                EngineVersion = historicalVersion,
                EffectiveGame = oldGame,
                NeonArcade = historicalBonus.Evidence
            },
            Payout = historicalBonus.Payout,
            NeonArcade = historicalBonus.Evidence
        };
        Assert.Empty(NeonOutcomeReplay.Check(historical, historical.Payout));
    }

    [Fact]
    public void CarPlaysPaysFiveButNotFourMatchingSymbolsOnLowerRoutes()
    {
        var result = SpinCarWithReels([
            ["6", "6", "6", "3", "6", "6", "7"],
            ["6", "6", "6", "6", "3", "6", "7"],
            ["6", "6", "6", "6", "6", "3", "7"],
            ["6", "6", "6", "6", "3", "6", "7"],
            ["6", "6", "6", "3", "6", "6", "6"]
        ]);

        var matches = result.Payout.Paylines.SelectMany(line => line.Matches).Select(paid => paid.Match).ToArray();
        Assert.Contains(matches, match => match.SymbolId == "3" &&
            match.Positions.SequenceEqual([new(0, 3), new(1, 4), new(2, 5), new(3, 4), new(4, 3)]));
        Assert.DoesNotContain(matches, match => match.MatchLength == 4);
        Assert.Empty(NeonOutcomeReplay.Check(result, result.Payout));
    }

    [Fact]
    public void TheFourCassetteZigzagOnlyPaysUnderTheHistoricalRules()
    {
        IReadOnlyList<IReadOnlyList<string>> reels =
        [
            ["SEAL_PAW", "3", "SEAL_PAW", "2"],
            ["3", "2", "7", "3"],
            ["5", "3", "3", "SEAL_ROWS"],
            ["3", "SEAL_SYNC", "SEAL_SYNC", "SEAL_SYNC"],
            ["5", "SEAL_PAW", "SEAL_SYNC", "SEAL_SYNC"]
        ];
        var game = NeonArcadeSlotDefinitions.CreateGame();
        var definitions = new NeonDefinitionSource();
        var symbols = definitions.GetSymbolSet(game.Symbols.SymbolSetId)!;
        var paytable = definitions.GetPaytable(game.Math.PaytableId)!;
        var evaluator = new NeonCombinationEvaluator();
        var calculator = new NeonPayoutCalculator();

        var previous = evaluator.Evaluate(reels, game, symbols, "neon-arcade-special-plays-v17");
        var historical = calculator.Calculate(previous, game, paytable, 300);
        var match = Assert.Single(Assert.Single(historical.Paylines).Matches);
        Assert.Equal(4, match.Match.MatchLength);
        Assert.Equal(230, match.AmountPoints);

        var current = evaluator.Evaluate(reels, game, symbols);
        Assert.Equal(0, calculator.Calculate(current, game, paytable, 300).TotalPoints);
    }

    [Fact]
    public void NitroSettlesWildPaylinesAndPreservesLandedCarAwards()
    {
        var result = SpinCarWithReels([
            ["7", "NITRO", "4", "4", "5", "7", "SEAL_SYNC"],
            ["7", "NITRO", "4", "5", "5", "4", "7"],
            ["2", "5", "FREE", "6", "BANANA", "BANANA", "BANANA"],
            ["4", "3", "7", "4", "7", "5", "4"],
            ["5", "7", "5", "7", "4", "7", "5"]
        ]);

        var evidence = Assert.IsType<NeonNitroWildOutcome>(result.NeonNitroWild);
        Assert.Equal(new GridPosition(0, 1), evidence.Trigger);
        Assert.Equal(1, evidence.EligibleReel);
        Assert.Equal(new GridPosition(1, 1), evidence.SecondNitro);
        Assert.Equal(2, evidence.ExpandedReel);
        Assert.Equal("NITRO", evidence.InitialReels[1][1]);
        Assert.Equal("NITRO", result.Reels[1][1]);
        Assert.All(result.Reels[2], symbol => Assert.Equal("ACE", symbol));
        Assert.Contains(result.Payout.Paylines.SelectMany(line => line.Matches),
            win => win.Match.PaylineId == 1 && win.Match.SymbolId == "7" &&
                win.Match.WildPositions.Contains(new GridPosition(2, 0)));
        Assert.Equal(1, result.NeonArcade.BonusSymbolCount);
        Assert.True(result.NeonArcade.PaylineBoostPoints > 0);
        Assert.Equal(7, result.FreeSpinsAwarded);
        Assert.Contains(new GridPosition(2, 3), Assert.Single(result.NeonArcade.Activators).Positions);
        Assert.Equal(1200, result.BananaBonusPoints);
        Assert.DoesNotContain(0, NeonCarRespin.EligibleReels(result));
        Assert.DoesNotContain(1, NeonCarRespin.EligibleReels(result));
        Assert.DoesNotContain(2, NeonCarRespin.EligibleReels(result));
        Assert.Empty(NeonOutcomeReplay.Check(result, result.Payout));
        Assert.Contains(NeonOutcomeReplay.Check(result with { NeonNitroWild = null }, result.Payout),
            finding => finding.Code == "slot-neon-nitro-wild-mismatch");
    }

    [Fact]
    public void LastTwoNitrosDeferPaymentUntilTheChosenReelTurnsWild()
    {
        var pending = SpinCarWithReels([
            ["7", "2", "4", "5", "5", "7", "SEAL_SYNC"],
            ["7", "5", "4", "2", "5", "4", "7"],
            ["2", "5", "7", "4", "5", "4", "7"],
            ["4", "NITRO", "7", "4", "7", "5", "4"],
            ["5", "7", "NITRO", "7", "4", "7", "5"]
        ]);

        Assert.True(pending.NeonNitroWild?.RequiresChoice);
        Assert.Equal(new GridPosition(4, 2), pending.NeonNitroWild?.SecondNitro);
        Assert.Null(pending.NeonNitroWild?.ExpandedReel);
        Assert.Equal(0, pending.Payout.TotalPoints);
        Assert.Empty(NeonCarRespin.EligibleReels(pending));
        Assert.Empty(NeonOutcomeReplay.Check(pending, pending.Payout));

        var resolved = NeonNightsEngine.ResolveNitroChoice(pending, 2);
        Assert.Equal(pending.SpinId, resolved.SpinId);
        Assert.Equal(pending.ReelStops, resolved.ReelStops);
        Assert.False(resolved.NeonNitroWild?.RequiresChoice);
        Assert.Equal(2, resolved.NeonNitroWild?.ExpandedReel);
        Assert.All(resolved.Reels[2], symbol => Assert.Equal("ACE", symbol));
        Assert.All(new[] { 0, 1, 3, 4 }, index => Assert.Equal(pending.Reels[index], resolved.Reels[index]));
        Assert.True(resolved.Payout.TotalPoints > 0);
        Assert.Empty(NeonOutcomeReplay.Check(resolved, resolved.Payout));
        Assert.Throws<ArgumentOutOfRangeException>(() => NeonNightsEngine.ResolveNitroChoice(pending, 5));
        Assert.Throws<InvalidOperationException>(() => NeonNightsEngine.ResolveNitroChoice(resolved, 1));
    }

    [Fact]
    public void LastReelRespinCanCompleteNitroChoiceWithoutRedrawing()
    {
        var original = SpinCarWithReels([
            ["7", "6", "6", "6", "6", "6", "6"],
            ["7", "6", "6", "6", "6", "6", "6"],
            ["2", "6", "6", "6", "6", "6", "6"],
            ["NITRO", "6", "6", "6", "6", "6", "6"],
            ["5", "6", "6", "6", "6", "6", "6", "NITRO", "6", "6", "6", "6", "6", "6"]
        ]);
        var pending = new NeonCarRespin(new Draws([7], 73)).Resolve(original, 4);
        Assert.True(pending.NeonNitroWild?.RequiresChoice);
        Assert.Equal(0, pending.ImprovementPoints);
        Assert.Empty(NeonCarRespin.Check(original, pending));

        var resolved = NeonCarRespin.ResolveChoice(original, pending, 2);
        Assert.Equal(pending.StopIndex, resolved.StopIndex);
        Assert.Equal(2, resolved.NeonNitroWild?.ExpandedReel);
        Assert.All(resolved.Reels[2], symbol => Assert.Equal("ACE", symbol));
        Assert.Equal("NITRO", resolved.Reels[4][0]);
        Assert.Equal(369, resolved.ImprovementPoints);
        Assert.Empty(NeonCarRespin.Check(original, resolved));
    }

    [Fact]
    public void FirstNitroArmsOnlyItsNeighborEvenWhenALaterPairLands()
    {
        var result = SpinCarWithReels([
            ["2", "NITRO", "NITRO", "4", "5", "7", "SEAL_SYNC"],
            ["2", "5", "4", "7", "5", "7", "SEAL_SYNC"],
            ["7", "NITRO", "4", "SEAL_SYNC", "5", "4", "7"],
            ["4", "3", "NITRO", "4", "7", "5", "4"],
            ["5", "NITRO", "5", "7", "4", "7", "5"]
        ]);

        var evidence = Assert.IsType<NeonNitroWildOutcome>(result.NeonNitroWild);
        Assert.Equal(new GridPosition(0, 1), evidence.Trigger);
        Assert.Equal(1, evidence.EligibleReel);
        Assert.Null(evidence.ExpandedReel);
        Assert.Equal("NITRO", result.Reels[3][2]);
        Assert.Empty(NeonOutcomeReplay.Check(result, result.Payout));
    }

    [Theory]
    [InlineData(false, 369)]
    [InlineData(true, 246)]
    public void CarRespinChangesOnlyTheSelectedReelAndPaysTheImprovement(bool historical, long improvement)
    {
        var original = SpinCarWithReels([
            ["7", "NITRO", "6", "6", "6", "6", "6"],
            ["2", "6", "6", "6", "6", "6", "6", "NITRO", "7", "6", "6", "6", "6", "6"],
            ["7", "6", "6", "6", "6", "6", "6"],
            ["4", "6", "6", "6", "6", "6", "6"],
            ["5", "6", "6", "6", "6", "6", "6"]
        ]);
        if (historical)
        {
            var audit = original.EngineAudit;
            static GameDefinition WithHistoricalScale(GameDefinition game)
            {
                var historical = JsonSerializer.SerializeToNode(game)!;
                historical["Math"]!["PayoutMultiplier"] = NeonArcadeSlotDefinitions.PreviousBasePayoutMultiplier;
                return historical.Deserialize<GameDefinition>()!;
            }

            original = original with
            {
                EngineAudit = audit with
                {
                    EngineVersion = NeonArcadeSlotDefinitions.PreviousNitroEngineVersion,
                    BaseGame = WithHistoricalScale(audit.BaseGame),
                    EffectiveGame = WithHistoricalScale(audit.EffectiveGame)
                }
            };
        }
        Assert.Equal(0, original.Payout.TotalPoints);
        Assert.Equal([0, 1, 2, 3, 4], NeonCarRespin.EligibleReels(original));

        var respin = new NeonCarRespin(new Draws([7], 73)).Resolve(original, 1);
        Assert.Equal(7, respin.StopIndex);
        Assert.Equal("NITRO", respin.InitialReels[1][0]);
        var expanded = historical ? 1 : 2;
        Assert.Equal(expanded, respin.NeonNitroWild?.ExpandedReel);
        Assert.All(respin.Reels[expanded], symbol => Assert.Equal("ACE", symbol));
        Assert.All(new[] { 0, 3, 4 }, reel => Assert.Equal(original.Reels[reel], respin.Reels[reel]));
        Assert.Equal(0, respin.OriginalPayoutPoints);
        Assert.Equal(improvement, respin.ImprovementPoints);
        Assert.Empty(NeonCarRespin.Check(original, respin));
        Assert.NotEmpty(NeonCarRespin.Check(original, respin with { ImprovementPoints = improvement + 1 }));
    }

    [Fact]
    public void CarRespinPreservesAnUnselectedWildReelWhenTheOriginalTriggerIsRemoved()
    {
        var original = SpinCarWithReels([
            ["NITRO", "6", "6", "6", "6", "6", "6", "2", "6", "6", "6", "6", "6", "6"],
            ["NITRO", "6", "6", "6", "6", "6", "6"],
            ["7", "6", "6", "6", "6", "6", "6"],
            ["4", "6", "6", "6", "6", "6", "6"],
            ["5", "6", "6", "6", "6", "6", "6"]
        ]);

        var respin = new NeonCarRespin(new Draws([7], 73)).Resolve(original, 0);
        Assert.Null(respin.NeonNitroWild);
        Assert.DoesNotContain("NITRO", respin.Reels[0]);
        Assert.Equal("NITRO", respin.Reels[1][0]);
        Assert.All(respin.Reels[2], symbol => Assert.Equal("ACE", symbol));
        Assert.Equal(0, respin.ImprovementPoints);
        Assert.Empty(NeonCarRespin.Check(original, respin));
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
        Assert.Contains(NeonOutcomeReplay.Check(disco with
        {
            EngineAudit = disco.EngineAudit with { EngineVersion = NeonArcadeSlotDefinitions.PreviousDiscoMathEngineVersion }
        }, disco.Payout), finding => finding.Code == "slot-neon-disco-math-mismatch");

        var previousPaytable = NeonDiscoDefinitions.CreatePreviousPairedPaytable();
        var historicalGame = JsonSerializer.SerializeToNode(disco.EngineAudit.EffectiveGame)!;
        historicalGame["Math"]!["PaytableId"] = previousPaytable.Id;
        var previousGame = historicalGame.Deserialize<GameDefinition>()!;
        var draws = new Queue<int>(disco.EngineAudit.NeonDiscoCascade.DrawIndexes);
        var previousCascade = NeonDiscoCascade.Resolve(disco.Reels, previousGame, disco.EngineAudit.ReelSet,
            disco.EngineAudit.SymbolSet, previousPaytable, disco.WagerPoints, new(), new(), _ => draws.Dequeue(),
            out _, 2);
        var previousPayout = new SpinPayout(previousCascade.TotalPoints, []);
        var previousDisco = disco with
        {
            PaytableId = previousPaytable.Id,
            Payout = previousPayout,
            NeonDiscoCascade = previousCascade,
            EngineAudit = disco.EngineAudit with
            {
                EngineVersion = NeonArcadeSlotDefinitions.PreviousDiscoMathEngineVersion,
                EffectiveGame = previousGame,
                Paytable = previousPaytable
            }
        };
        Assert.True(previousPayout.TotalPoints > disco.Payout.TotalPoints);
        Assert.Empty(NeonOutcomeReplay.Check(previousDisco, previousPayout));
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

    private static NeonSpinOutcome SpinCarWithReels(List<List<string>> reels) =>
        new NeonNightsEngine(new CarFixtureDefinitions(reels), new Draws([0, 0, 0, 0, 0], 73))
            .Spin(100, "nitro", false, 0, NeonArcadeBonus.PaidCarFeatureMode, 0, true);

    private sealed class CarFixtureDefinitions(List<List<string>> reels) : ISlotDefinitionSource
    {
        private readonly NeonDefinitionSource definitions = new();

        public GameDefinition? GetGame(string id) => definitions.GetGame(id);
        public SymbolSetDefinition? GetSymbolSet(string id) => definitions.GetSymbolSet(id);
        public PaytableDefinition? GetPaytable(string id) => definitions.GetPaytable(id);
        public ReelSetDefinition? GetReelSet(string id) => id == NeonArcadeSlotDefinitions.CarReelSetId
            ? new() { Id = id, SymbolSetId = NeonArcadeSlotDefinitions.CarSymbolSetId, Reels = reels }
            : definitions.GetReelSet(id);
    }

    private sealed class Draws(int[] stops, int seed) : IGameRandomSource
    {
        private int cursor;
        private readonly Random random = new(seed);
        public int Next(int maximumExclusive) => cursor < stops.Length
            ? stops[cursor++] : random.Next(maximumExclusive);
    }
}
