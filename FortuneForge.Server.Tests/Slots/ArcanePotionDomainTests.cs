// Edited: 2026-11-18T18:01:48-06:00 | Computer: Vinnabok
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.Slots;
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
    [InlineData(Root, 10, 20)]
    [InlineData(Wine, 9, 21)]
    public void PaylinePotionBoostsOnlyItsOwnPayingGlyphs(string ingredient, long ogham, long premium)
    {
        SpinPayout payout = new(38,
        [
            new(1, 9, [new(new(1, "2", 3, [], []), 1, 9)]),
            new(2, 20, [new(new(2, "5", 3, [], []), 2, 20)]),
            new(3, 9, [new(new(3, "2", 3, [], []), 1, 9)])
        ]);
        var boosted = ArcanePotions.BoostPaylines(payout, new(1, ingredient, 0));
        Assert.Equal(new[] { ogham, premium, 9 }, boosted.Paylines.Select(line => line.AmountPoints));
        Assert.Equal(ogham + premium + 9, boosted.TotalPoints);
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

    private sealed class ShiftRandom(int shift) : IGameRandomSource
    {
        public int Next(int maximumExclusive) => maximumExclusive == 5 ? shift : 0;
    }
}
