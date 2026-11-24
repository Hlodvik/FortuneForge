// Edited: 2026-11-18T18:02:58-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:55:38-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:46:41-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:32:58-06:00 | Computer: Vinnabok
using System.Text.Json;
using FortuneForge.Games.ArcaneArchives;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class ArcaneTomeTests
{
    [Theory]
    [InlineData("herbalism", "MANDRAKE_ROOT", "NIGHTSHADE_BERRIES", "QUICKSILVER")]
    [InlineData("moon-and-flame", "QUICKSILVER", "PHOENIX_FEATHER", "MOONCAP_MUSHROOM")]
    [InlineData("distillation", "MOONCAP_MUSHROOM", "WOLFSBANE_WINE", "QUICKSILVER")]
    public void AwakeningRequiresAGrimoireAndDoublesOnlyTheSelectedPairForFivePaidSpins(
        string page, string first, string second, string ordinary)
    {
        var selected = ArcaneTome.SelectPage(ArcaneTome.Empty, page, activate: false);
        Assert.Throws<ArcaneTomeConflictException>(() => ArcaneTome.SelectPage(selected, page, activate: true));
        var earned = ArcaneTome.CompleteSpin(selected, [[first, ArcaneTome.GrimoireSymbolId]], false);
        Assert.True(earned.State.GrimoireReady);
        Assert.Null(earned.State.ActivePage);
        Assert.Equal(1, earned.IngredientsAwarded[first]);
        Assert.Throws<ArcaneTomeActivationRequiredException>(() => ArcaneTome.EnsureSpinAllowed(earned.State));

        var active = ArcaneTome.SelectPage(earned.State, page, activate: true);
        Assert.Equal(5, active.BoostedSpinsRemaining);
        Assert.False(active.GrimoireReady);
        ArcaneTome.EnsureSpinAllowed(active);
        Assert.Same(active, ArcaneTome.SelectPage(active, page, activate: true));
        var otherPage = page == "herbalism" ? "distillation" : "herbalism";
        Assert.Throws<ArcaneTomeConflictException>(() => ArcaneTome.SelectPage(active, otherPage, activate: false));
        for (var remaining = 4; remaining >= 0; remaining--)
        {
            var progress = ArcaneTome.CompleteSpin(active, [[first, second, ordinary]], false);
            ArcaneTome.ValidateState(progress.State);
            Assert.Equal(2, progress.IngredientsAwarded[first]);
            Assert.Equal(2, progress.IngredientsAwarded[second]);
            Assert.Equal(1, progress.IngredientsAwarded[ordinary]);
            Assert.Equal(remaining, progress.State.BoostedSpinsRemaining);
            Assert.Equal(remaining == 0 ? null : page, progress.State.ActivePage);
            active = progress.State;
        }

        Assert.Equal(11, active.Ingredients[first]);
        Assert.Equal(10, active.Ingredients[second]);
        Assert.Equal(5, active.Ingredients[ordinary]);
        var unboosted = ArcaneTome.CompleteSpin(active, [[first, first]], false);
        Assert.Equal(2, unboosted.IngredientsAwarded[first]);
        Assert.Equal(13, unboosted.State.Ingredients[first]);
    }

    [Fact]
    public void SavedLegacyBoostKeepsItsRecipeUntilItExpires()
    {
        const string page = "moon-and-flame";
        var saved = ArcaneTome.Empty with { SelectedPage = page, ActivePage = page, BoostedSpinsRemaining = 1 };
        var json = JsonSerializer.Serialize(saved);
        Assert.DoesNotContain("RecipeVersion", json);
        var reloaded = JsonSerializer.Deserialize<ArcaneTomeState>(json)!;
        Assert.True(ArcaneTome.SameState(saved, reloaded));
        Assert.Equal("Moon & Flame", ArcaneTome.ToView(reloaded)!.Pages.Single(item => item.Id == page).Label);

        IReadOnlyList<IReadOnlyList<string>> reels = [["MOONCAP_MUSHROOM", "PHOENIX_FEATHER", "QUICKSILVER"]];
        var finished = ArcaneTome.CompleteSpin(reloaded, reels, false);
        Assert.Equal(2, finished.IngredientsAwarded["MOONCAP_MUSHROOM"]);
        Assert.Equal(1, finished.IngredientsAwarded["QUICKSILVER"]);
        Assert.Equal("Transmutation", ArcaneTome.ToView(finished.State)!.Pages.Single(item => item.Id == page).Label);

        var nextBoost = ArcaneTome.SelectPage(finished.State with { GrimoireReady = true }, page, true);
        var restored = JsonSerializer.Deserialize<ArcaneTomeState>(JsonSerializer.Serialize(nextBoost))!;
        Assert.True(ArcaneTome.SameState(nextBoost, restored));
        var updated = ArcaneTome.CompleteSpin(restored, reels, false);
        Assert.Equal(1, updated.IngredientsAwarded["MOONCAP_MUSHROOM"]);
        Assert.Equal(2, updated.IngredientsAwarded["QUICKSILVER"]);
    }

    [Fact]
    public void ZeroIngredientsCanLandOnConsecutiveSpinsAcrossTomeActivation()
    {
        var dry = ArcaneTome.CompleteSpin(ArcaneTome.Empty, [["2", ArcaneTome.GrimoireSymbolId]], false);
        Assert.Empty(dry.IngredientsAwarded);
        Assert.Equal(ArcaneTome.Empty.Ingredients, dry.State.Ingredients);

        var activated = ArcaneTome.SelectPage(dry.State, "herbalism", activate: true);
        var boostedDry = ArcaneTome.CompleteSpin(activated, [["2"]], false);
        Assert.Empty(boostedDry.IngredientsAwarded);
        Assert.Equal(activated.Ingredients, boostedDry.State.Ingredients);
        Assert.Equal(ArcaneTome.BoostedSpinCount - 1, boostedDry.State.BoostedSpinsRemaining);
        Assert.Throws<InvalidOperationException>(() =>
            ArcaneTome.CompleteSpin(activated, [["2"]], false, requireIngredient: true));
    }

    [Fact]
    public void BoostedReelModelRemovesAllGrimoiresAndKeepsTheIngredientPositions()
    {
        var regular = ArcaneArchivesEngine.CreateReelSet();
        var boosted = ArcaneArchivesEngine.CreateTomeReelSet();
        Assert.Equal(ArcaneArchivesEngine.TomeReelSetId, boosted.Id);
        Assert.Equal(regular.SymbolSetId, boosted.SymbolSetId);
        Assert.Contains(ArcaneTome.GrimoireSymbolId, regular.Reels.SelectMany(reel => reel));
        for (var reel = 0; reel < regular.Reels.Count; reel++)
        {
            var ordinaryStrip = regular.Reels[reel];
            var boostedStrip = boosted.Reels[reel];
            Assert.Equal(ordinaryStrip.Count, boostedStrip.Count);
            Assert.DoesNotContain(ArcaneTome.GrimoireSymbolId, boostedStrip);
            for (var stop = 0; stop < ordinaryStrip.Count; stop++)
            {
                if (ordinaryStrip[stop] != ArcaneTome.GrimoireSymbolId)
                    Assert.Equal(ordinaryStrip[stop], boostedStrip[stop]);
            }
        }
    }
}
