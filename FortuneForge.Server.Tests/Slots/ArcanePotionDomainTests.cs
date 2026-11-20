// Edited: 2026-11-18T18:01:48-06:00 | Computer: Vinnabok
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.Slots;
using System.Text.Json;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class ArcanePotionDomainTests
{
    private const string Root = "MANDRAKE_ROOT";
    private const string Berries = "NIGHTSHADE_BERRIES";
    private const string Mooncap = "MOONCAP_MUSHROOM";
    private const string Feather = "PHOENIX_FEATHER";
    private const string Wine = "WOLFSBANE_WINE";
    private const string Silver = "QUICKSILVER";

    [Fact]
    public void FifthOfAKindBottlesEarlyAndLaterUnitsCarryOver()
    {
        var state = ArcanePotions.CompleteSpin(ArcanePotions.Empty,
            [Root, Silver, Root, Root, Root, Root, Silver], false);

        Assert.Equal(new ArcanePotionBottle(1, Root, 3), Assert.Single(state.Bottles));
        Assert.Equal(new[] { Silver }, state.Pot);
        Assert.Equal(2, state.NextBottleId);
    }

    [Fact]
    public void PotionWagerWeightsEachPaidUnitAndRoundsDownOnBottling()
    {
        var pot = ArcanePotions.CompleteSpin(ArcanePotions.Empty, [Root, Root], false, wagerPoints: 6);
        pot = ArcanePotions.CompleteSpin(pot, [Mooncap, Root], false, wagerPoints: 14);
        var restored = JsonSerializer.Deserialize<ArcanePotionState>(JsonSerializer.Serialize(pot))!;
        Assert.True(ArcanePotions.SameState(pot, restored));
        var brewed = ArcanePotions.CompleteSpin(restored, [Root, Root, Silver], false, wagerPoints: 20);

        var bottle = Assert.Single(brewed.Bottles);
        Assert.Equal(12, bottle.LockedWagerPoints);
        Assert.Equal(new ArcanePotWager(20, 1), brewed.PotWager);
        var drinking = ArcanePotions.Use(brewed, bottle.Id);
        Assert.Null(ArcanePotions.LockedWagerForSpin(drinking, isFreeSpin: true));
        drinking = ArcanePotions.CompleteSpin(drinking, [], true, wagerPoints: 32);
        for (var remaining = ArcanePotions.Duration; remaining > 0; remaining--)
        {
            Assert.Equal(12, ArcanePotions.LockedWagerForSpin(drinking, isFreeSpin: false));
            Assert.Equal(remaining, drinking.Active!.SpinsRemaining);
            drinking = ArcanePotions.CompleteSpin(drinking, [], false, wagerPoints: 12);
        }
        Assert.Null(ArcanePotions.LockedWagerForSpin(drinking, isFreeSpin: false));
    }

    [Fact]
    public void KnownFreeUnitsDoNotEnterTheMeanAndUnknownLegacyUnitsUseTheMinimum()
    {
        var freePot = ArcanePotions.CompleteSpin(ArcanePotions.Empty, [Root, Root, Root, Root], true, wagerPoints: 100);
        var mixed = ArcanePotions.CompleteSpin(freePot, [Root], false, wagerPoints: 12);
        Assert.Equal(12, Assert.Single(mixed.Bottles).LockedWagerPoints);
        var allFree = ArcanePotions.CompleteSpin(freePot, [Root], true, wagerPoints: 100);
        Assert.Equal(2, Assert.Single(allFree.Bottles).LockedWagerPoints);

        var oldPot = ArcanePotions.CompleteSpin(ArcanePotions.Empty, [Root, Root, Root, Root], false);
        Assert.DoesNotContain("PotWager", JsonSerializer.Serialize(oldPot));
        var oldBottle = Assert.Single(ArcanePotions.CompleteSpin(oldPot, [Root], false).Bottles);
        Assert.DoesNotContain("LockedWagerPoints", JsonSerializer.Serialize(oldBottle));
        Assert.Equal(2, ArcanePotions.LockedWagerPoints(oldBottle));
        var upgraded = ArcanePotions.CompleteSpin(oldPot, [Root], false, wagerPoints: 100);
        Assert.Equal(20, Assert.Single(upgraded.Bottles).LockedWagerPoints);
    }

    [Theory]
    [InlineData(Silver, ArcanePotions.AstralConvergence, "Astral Convergence")]
    [InlineData(Wine, ArcanePotions.RuneboundElixir, "Runebound Elixir")]
    public void ExactFourPairsWithMandrakeSelectTheRareVariantAndKeepTheEarnedWager(
        string fourth, string variant, string name)
    {
        var pot = ArcanePotions.CompleteSpin(ArcanePotions.Empty,
            [Root, Root, Mooncap, Feather, Mooncap, Feather, fourth], false, wagerPoints: 6);
        var brewed = ArcanePotions.CompleteSpin(pot, [fourth, Berries], false, wagerPoints: 10);
        var bottle = Assert.Single(brewed.Bottles);
        Assert.Equal(variant, bottle.VariantId);
        Assert.Equal(Root, bottle.IngredientId);
        Assert.Equal(name, ArcanePotions.Describe(bottle).Name);
        Assert.Equal(6, bottle.LockedWagerPoints);
        Assert.Equal(Berries, Assert.Single(brewed.Pot));
        Assert.Equal(new ArcanePotWager(10, 1), brewed.PotWager);
        var active = ArcanePotions.Use(brewed, bottle.Id);
        Assert.Equal(ArcanePotions.Duration, active.Active!.SpinsRemaining);
        Assert.Equal(6, ArcanePotions.LockedWagerForSpin(active, false));
        Assert.Equal(variant, ArcanePotions.ForSpin(active, false)!.VariantId);

        var oldRules = ArcanePotions.CompleteSpin(pot, [fourth], false, wagerPoints: 10, allowRareRecipes: false);
        var oldBottle = Assert.Single(oldRules.Bottles);
        Assert.Null(oldBottle.VariantId);
        Assert.Equal(fourth, oldBottle.IngredientId);
        Assert.Equal(6, oldBottle.LockedWagerPoints);
    }

    [Fact]
    public void OtherFullPotsBrewTheNormalMajorityPotion()
    {
        var missingMandrake = ArcanePotions.CompleteSpin(ArcanePotions.Empty,
            [Berries, Berries, Mooncap, Mooncap, Feather, Feather, Silver, Silver], false, wagerPoints: 10);
        Assert.Null(Assert.Single(missingMandrake.Bottles).VariantId);
        var unequalPairs = ArcanePotions.CompleteSpin(ArcanePotions.Empty,
            [Root, Root, Root, Mooncap, Feather, Feather, Silver, Silver], false, wagerPoints: 10);
        var bottle = Assert.Single(unequalPairs.Bottles);
        Assert.Null(bottle.VariantId);
        Assert.Equal(Root, bottle.IngredientId);
    }

    [Fact]
    public void FullPotUsesMostRecentTiedLeaderAndAddsOnlyOtherIngredientsPotency()
    {
        var units = new[] { Root, Berries, Root, Berries, Root, Berries, Mooncap, Silver };
        var before = ArcanePotions.CompleteSpin(ArcanePotions.Empty, units[..7], false);
        var preview = Assert.IsType<ArcanePotionDescription>(ArcanePotions.Preview(before));
        Assert.Equal(Berries, preview.IngredientId);
        Assert.Equal(5, preview.Potency);

        var after = ArcanePotions.CompleteSpin(before, units[7..], false);
        Assert.Equal(new ArcanePotionBottle(1, Berries, 8), Assert.Single(after.Bottles));
        Assert.Empty(after.Pot);
    }

    [Fact]
    public void TomeUnitsStayAdjacentInReelOrder()
    {
        var tome = ArcaneTome.SelectPage(ArcaneTome.Empty with { GrimoireReady = true }, "herbalism", true);
        IReadOnlyList<IReadOnlyList<string>> reels = [[Root, "2"], [Mooncap, Berries]];
        Assert.Equal(new[] { Root, Root, Mooncap, Berries, Berries }, ArcaneTome.OrderedIngredientUnits(tome, reels, false));
        Assert.Equal(new[] { Root, Mooncap, Berries }, ArcaneTome.OrderedIngredientUnits(tome, reels, true));
    }

    [Fact]
    public void AChargeAppliesToThreePaidSpinsAndPreventsStacking()
    {
        var shelf = ArcanePotions.CompleteSpin(ArcanePotions.Empty,
            [Root, Root, Root, Root, Root, Wine, Wine, Wine, Wine, Wine], false);
        var drinking = ArcanePotions.Use(shelf, 1);
        Assert.Equal(2, Assert.Single(drinking.Bottles).Id);
        Assert.Throws<ArcanePotionConflictException>(() => ArcanePotions.Use(drinking, 2));
        Assert.Equal(drinking.Active!.Bottle, ArcanePotions.ForSpin(drinking, false));
        Assert.Null(ArcanePotions.ForSpin(drinking, true));
        Assert.Equal(3, ArcanePotions.CompleteSpin(drinking, [], true).Active!.SpinsRemaining);

        drinking = ArcanePotions.CompleteSpin(drinking, [], false);
        Assert.Equal(2, drinking.Active!.SpinsRemaining);
        drinking = ArcanePotions.CompleteSpin(drinking, [], false);
        Assert.Equal(1, drinking.Active!.SpinsRemaining);
        drinking = ArcanePotions.CompleteSpin(drinking, [], false);
        Assert.Null(drinking.Active);
        Assert.Equal(3, ArcanePotions.Use(drinking, 2).Active!.SpinsRemaining);
    }

    [Theory]
    [InlineData(Root, false, 12, 14, 20, 9)]
    [InlineData(Root, false, 2, 11, 20, 11)]
    [InlineData(Root, true, 12, 10, 20, 9)]
    [InlineData(Wine, false, 12, 9, 21, 9)]
    public void PaylinePotionBoostsOnlyItsOwnPayingGlyphs(
        string ingredient, bool useLegacyMandrake, long wagerPoints, long firstScribe, long premium, long secondScribe)
    {
        SpinPayout payout = new(38,
        [
            new(1, 9, [new(new(1, "2", 3, [], []), 1, 9)]),
            new(2, 20, [new(new(2, "5", 3, [], []), 2, 20)]),
            new(3, 9, [new(new(3, "2", 3, [], []), 1, 9)])
        ]);
        var boosted = ArcanePotions.BoostPaylines(payout, new(1, ingredient, 0), wagerPoints, useLegacyMandrake);
        Assert.Equal(new[] { firstScribe, premium, secondScribe }, boosted.Paylines.Select(line => line.AmountPoints));
        Assert.Equal(firstScribe + premium + secondScribe, boosted.TotalPoints);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(18, 23)]
    public void MandrakeTonicReportsItsPayoutBoostAndMinimum(int potency, int percentage)
    {
        var effect = ArcanePotions.Describe(Root, potency).Effect;

        Assert.Equal($"Scribe Mark payline value +{percentage}% (minimum 1–5 points per qualifying spin, based on wager)", effect);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(4, 3)]
    [InlineData(12, 6)]
    public void MandrakePaysItsWagerBasedFloorOnSmallQualifyingWins(long wagerPoints, long expected)
    {
        SpinPayout payout = new(1, [new(1, 1, [new(new(1, "2", 3, [], []), 1, 1)])]);

        Assert.Equal(expected, ArcanePotions.BoostPaylines(payout, new(1, Root, 0), wagerPoints).TotalPoints);
    }

    [Fact]
    public void WandPotionPreservesNominalMultipliersAndCreditsTheExplicitBonus()
    {
        var potion = new ArcanePotionBottle(1, Silver, 18);
        var collection = ArcaneWandCollector.Calculate([["WAND", "POWER"], ["BOLT", "FORTUNE_RUNE"]], 12, potion)!;
        Assert.Equal(new long[] { 2, 4, 10 }, collection.Cells.Select(cell => cell.Multiplier));
        Assert.Equal(new long[] { 1, 3, 6 }, collection.Cells.Select(cell => cell.PotionBonusPoints));
        Assert.Equal(202, collection.TotalPoints);
        Assert.Equal(202, ArcaneWandCollector.CreatePayout(collection).AmountPoints);

        var minimum = ArcaneWandCollector.Calculate([["WAND", "POWER"]], 12, new(1, Silver, 0))!;
        Assert.Equal(1, Assert.Single(minimum.Cells).PotionBonusPoints);
    }

    [Fact]
    public void FreeSpinUsesBaseStopsEvenWhenGivenAPotion()
    {
        var engine = new ArcaneArchivesEngine(new ShiftRandom(3));
        var bottle = new ArcanePotionBottle(1, Feather, 18);
        var free = engine.SpinTome(12, "player", 0, 0, true, true, bottle);
        var paid = engine.SpinTome(12, "player", 0, 0, false, true, bottle);

        Assert.True(ArcaneArchivesEngine.PotionReelsMatch(free.Audit.ReelSet, null));
        Assert.True(ArcaneArchivesEngine.PotionReelsMatch(paid.Audit.ReelSet, bottle));
        Assert.False(ArcaneArchivesEngine.PotionReelsMatch(free.Audit.ReelSet, bottle));
    }

    [Theory]
    [InlineData(Mooncap, "5,6")]
    [InlineData(Feather, "WAND")]
    [InlineData(Berries, "POWER,BOLT,FORTUNE_RUNE")]
    public void FrequencyPotionProtectsSpecialAndIngredientStopsInEveryLayout(string ingredient, string targetList)
    {
        var targets = targetList.Split(',');
        var bottle = new ArcanePotionBottle(1, ingredient, 18);
        foreach (var tomeActive in new[] { false, true })
        for (var shift = 0; shift < 5; shift++)
        {
            var engine = new ArcaneArchivesEngine(new ShiftRandom(shift));
            var result = engine.SpinTome(12, "player", 0, 0, false, tomeActive, bottle);
            var baseReels = ArcaneArchivesEngine.FindReelSet(result.ReelSetId)!;
            var effective = result.Audit.ReelSet;

            for (var reel = 0; reel < baseReels.Reels.Count; reel++)
            {
                var original = baseReels.Reels[reel];
                var altered = effective.Reels[reel];
                for (var stop = 0; stop < altered.Count; stop++)
                {
                    var symbol = original[stop % original.Count];
                    if (symbol is not ("2" or "3" or "4"))
                        Assert.Equal(symbol, altered[stop]);
                }
                if (tomeActive)
                    Assert.DoesNotContain(ArcaneTome.GrimoireSymbolId, altered);
            }
            for (var index = 0; index < targets.Length; index++)
            {
                var target = targets[index];
                var reel = target == ArcaneWandCollector.WandSymbolId ? baseReels.Reels.Count - 1 : index;
                Assert.Equal(3 + 4 * baseReels.Reels[reel].Count(symbol => symbol == target),
                    effective.Reels[reel].Count(symbol => symbol == target));
            }

            Assert.True(ArcaneArchivesEngine.PotionReelsMatch(effective, bottle));
            Assert.False(ArcaneArchivesEngine.PotionReelsMatch(effective, null));
        }
    }

    [Theory]
    [InlineData(ArcanePotions.AstralConvergence, "WAND,POWER,BOLT,FORTUNE_RUNE")]
    [InlineData(ArcanePotions.RuneboundElixir, "5,6")]
    public void RarePotionsAddTheirTargetsAndPreserveSpecialStopsAcrossAllLayouts(string variant, string symbols)
    {
        var targets = symbols.Split(',');
        var potion = new ArcanePotionBottle(1, Root, 10) { VariantId = variant };
        foreach (var tomeActive in new[] { false, true })
        for (var shift = 0; shift < 5; shift++)
        {
            var engine = new ArcaneArchivesEngine(new ShiftRandom(shift));
            var result = engine.SpinTome(12, "player", 0, 0, false, tomeActive, potion);
            var baseline = ArcaneArchivesEngine.FindReelSet(result.ReelSetId)!;
            var effective = result.Audit.ReelSet;
            for (var reel = 0; reel < baseline.Reels.Count; reel++)
            {
                var original = baseline.Reels[reel];
                var boosted = effective.Reels[reel];
                Assert.Equal(original.Count, boosted.Count);
                for (var stop = 0; stop < original.Count; stop++)
                    if (original[stop] is not ("2" or "3" or "4"))
                        Assert.Equal(original[stop], boosted[stop]);
            }
            foreach (var target in targets)
                Assert.True(effective.Reels.Sum(reel => reel.Count(symbol => symbol == target)) >
                    baseline.Reels.Sum(reel => reel.Count(symbol => symbol == target)));
            Assert.True(ArcaneArchivesEngine.PotionReelsMatch(effective, potion));
            Assert.False(ArcaneArchivesEngine.PotionReelsMatch(effective, null));
        }
        var oldReels = ArcaneArchivesEngine.FindReelSet("arcane-archives-payline-reels-v7")!;
        Assert.False(ArcaneArchivesEngine.PotionReelsMatch(oldReels, potion));
    }

    [Fact]
    public void AstralBoostsWandCollectionsWhileRuneboundBoostsAllAndOnlyPaylines()
    {
        var astral = new ArcanePotionBottle(1, Root, 10) { VariantId = ArcanePotions.AstralConvergence };
        var runebound = astral with { VariantId = ArcanePotions.RuneboundElixir };
        IReadOnlyList<IReadOnlyList<string>> reels = [["WAND", "POWER"], ["BOLT", "FORTUNE_RUNE"]];
        var collection = ArcaneWandCollector.Calculate(reels, 12)!;
        Assert.All(ArcaneWandCollector.Calculate(reels, 12, astral)!.Cells,
            cell => Assert.True(cell.PotionBonusPoints > 0));
        Assert.True(ArcaneWandCollector.SameCollection(collection, ArcaneWandCollector.Calculate(reels, 12, runebound)));

        var wandAward = ArcaneWandCollector.CreatePayout(collection);
        SpinPayout payout = new(17 + wandAward.AmountPoints,
        [
            new(1, 8, [new(new(1, "2", 3, [], []), 1, 8)]),
            new(2, 9, [new(new(2, "5", 3, [], []), 1, 9)]),
            wandAward
        ]);
        Assert.Same(payout, ArcanePotions.BoostPaylines(payout, astral, 12));
        var boosted = ArcanePotions.BoostPaylines(payout, runebound, 12);
        Assert.True(boosted.Paylines[0].AmountPoints > 8);
        Assert.True(boosted.Paylines[1].AmountPoints > 9);
        Assert.Equal(wandAward.AmountPoints, boosted.Paylines[2].AmountPoints);
    }

    private sealed class ShiftRandom(int shift) : IGameRandomSource
    {
        public int Next(int maximumExclusive) => maximumExclusive == 5 ? shift : 0;
    }
}
