using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Reels;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class SlotSpecialRoundProfilesTests
{
    [Theory]
    [InlineData(SlotSpecialRoundProfiles.CosmicFortuneGameId, 3, 6, 24, 7)]
    [InlineData(SlotSpecialRoundProfiles.HighNoonFortuneGameId, 3, 5, 20, 8)]
    [InlineData(SlotSpecialRoundProfiles.GodsOfOlympusGameId, 4, 6, 28, 6)]
    [InlineData(SlotSpecialRoundProfiles.PiratesFortuneGameId, 3, 7, 15, 10)]
    [InlineData(SlotSpecialRoundProfiles.RoyalDrawGameId, 3, 6, 24, 7)]
    [InlineData(SlotSpecialRoundProfiles.SamuraiFortuneGameId, 4, 6, 26, 7)]
    [InlineData(SlotSpecialRoundProfiles.RobotRevolutionGameId, 3, 7, 24, 8)]
    [InlineData(SlotSpecialRoundProfiles.PhantomManorGameId, 3, 5, 18, 9)]
    [InlineData(SlotSpecialRoundProfiles.OceanOdysseyGameId, 4, 6, 28, 6)]
    [InlineData(SlotSpecialRoundProfiles.DragonHoardGameId, 3, 6, 30, 7)]
    [InlineData(SlotSpecialRoundProfiles.JungleJackpotGameId, 3, 6, 22, 8)]
    [InlineData(SlotSpecialRoundProfiles.CandyCarnivalGameId, 4, 5, 20, 8)]
    [InlineData(SlotSpecialRoundProfiles.DesertTreasuresGameId, 3, 7, 24, 7)]
    [InlineData(SlotSpecialRoundProfiles.NeonNightsGameId, 3, 6, 26, 7)]
    [InlineData(SlotSpecialRoundProfiles.NordicLegendsGameId, 4, 6, 28, 6)]
    [InlineData(SlotSpecialRoundProfiles.ReelRichesGameId, 3, 5, 40, 10)]
    [InlineData(SlotSpecialRoundProfiles.ArcaneArchivesGameId, 3, 5, 40, 10)]
    [InlineData(SlotSpecialRoundProfiles.DinoDominionGameId, 3, 5, 40, 10)]
    [InlineData(SlotSpecialRoundProfiles.RainbowRealmGameId, 3, 5, 60, 8)]
    public void FeaturedProfiles_ExposeTheirOwnScatterAndCollectionRules(
        string gameId,
        int scatterCount,
        int scatterAward,
        int collectionTarget,
        int collectionAward)
    {
        Assert.True(SlotSpecialRoundProfiles.TryGet(gameId, out var profile));

        Assert.Equal(scatterCount, profile.ScatterRequiredSymbols);
        Assert.Equal(scatterAward, profile.ScatterAwardedSpins);
        Assert.Equal(collectionTarget, profile.CollectionTarget);
        Assert.Equal(collectionAward, profile.CollectionAwardedSpins);
    }

    [Fact]
    public void FeaturedProfiles_VaryEnergyCollectionsAndDirectValueTokens()
    {
        var profiles = new[]
        {
            SlotSpecialRoundProfiles.CosmicFortuneGameId,
            SlotSpecialRoundProfiles.HighNoonFortuneGameId,
            SlotSpecialRoundProfiles.GodsOfOlympusGameId,
            SlotSpecialRoundProfiles.PiratesFortuneGameId,
            SlotSpecialRoundProfiles.RoyalDrawGameId,
            SlotSpecialRoundProfiles.SamuraiFortuneGameId,
            SlotSpecialRoundProfiles.RobotRevolutionGameId,
            SlotSpecialRoundProfiles.PhantomManorGameId,
            SlotSpecialRoundProfiles.OceanOdysseyGameId,
            SlotSpecialRoundProfiles.DragonHoardGameId,
            SlotSpecialRoundProfiles.JungleJackpotGameId,
            SlotSpecialRoundProfiles.CandyCarnivalGameId,
            SlotSpecialRoundProfiles.DesertTreasuresGameId,
            SlotSpecialRoundProfiles.NeonNightsGameId,
            SlotSpecialRoundProfiles.NordicLegendsGameId,
            SlotSpecialRoundProfiles.ReelRichesGameId,
            SlotSpecialRoundProfiles.ArcaneArchivesGameId,
            SlotSpecialRoundProfiles.DinoDominionGameId,
            SlotSpecialRoundProfiles.RainbowRealmGameId
        }
        .Select(gameId =>
        {
            Assert.True(SlotSpecialRoundProfiles.TryGet(gameId, out var profile));
            return profile;
        })
        .ToArray();

        Assert.Equal(8, profiles.Count(profile => profile.UsesEnergy));
        Assert.Equal(10, profiles.Count(profile => !profile.UsesDirectValueTokens));
        Assert.Equal(5, profiles.Count(profile => !profile.UsesCollections));
        Assert.All(profiles.Where(profile => !profile.UsesCollections), profile =>
            Assert.True(SlotSpecialRoundProfiles.IsFeatureMode(profile.ScatterFeatureMode)));
        Assert.All(profiles, profile =>
            Assert.True(
                profile.UsesCollections ||
                SlotSpecialRoundProfiles.IsFeatureMode(profile.ScatterFeatureMode)));
        Assert.All(
            profiles
                .GroupBy(profile => profile.CollectionFeatureMode ?? profile.ScatterFeatureMode)
                .Select(group => group.Count()),
            count => Assert.InRange(count, 1, 2));
    }

    [Fact]
    public void CatalogProfiles_ExposeEverySlotExactlyOnce()
    {
        Assert.Equal(20, SlotSpecialRoundProfiles.All.Count);
        Assert.Equal(
            SlotSpecialRoundProfiles.All.Count,
            SlotSpecialRoundProfiles.All.Select(profile => profile.GameId).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(SlotSpecialRoundProfiles.ReelRichesGameId, 19)]
    [InlineData(SlotSpecialRoundProfiles.ArcaneArchivesGameId, 16)]
    [InlineData(SlotSpecialRoundProfiles.DinoDominionGameId, 14)]
    public void OptionsProvider_AppliesTheClientPaylineSelection(string gameId, int expectedCount)
    {
        var provider = new OptionsSlotsDefinitionProvider(Options.Create(new SlotsOptions
        {
            GameDefinitions = [Prototype()]
        }));

        var game = Assert.IsType<GameDefinition>(provider.GetGame(gameId));

        Assert.Equal(expectedCount, game.Layout.PaylineCount);
        Assert.Equal(expectedCount, game.Paylines.Count);
        Assert.Equal(expectedCount, game.Math.PaylinePayoutSteps.Count);
    }

    [Fact]
    public void DemoSettlement_CompletesACollectionIntoTheCorrectEnhancedRound()
    {
        SlotSpecialRoundProfiles.TryGet(SlotSpecialRoundProfiles.CosmicFortuneGameId, out var profile);

        var result = SlotSpecialRoundProfiles.SettleDemo(
            profile,
            [new SlotSealCollection("sync", 23, 40, 24)],
            new Dictionary<string, int> { ["SEAL_SYNC"] = 1 },
            wagerPoints: 40,
            energyCompleted: false);

        Assert.Equal(7, result.FreeSpinsAwarded);
        Assert.Equal("sync-rows", result.FeatureMode);
        Assert.Contains(result.Collections, collection =>
            collection.SealId == "sync" && collection.Count == 0 && collection.RequiredCount == 24);
    }

    [Fact]
    public void OptionsProvider_AppliesThePiratesSpecificBaseReelSet()
    {
        var provider = new OptionsSlotsDefinitionProvider(Options.Create(new SlotsOptions
        {
            GameDefinitions = [Prototype()]
        }));

        var game = provider.GetGame(SlotSpecialRoundProfiles.PiratesFortuneGameId);

        Assert.NotNull(game);
        Assert.Equal(SlotSpecialRoundProfiles.PiratesFortuneGameId, game.Id);
        Assert.Equal(3, game.FreeGames?.RequiredSymbols);
        Assert.Equal(7, game.FreeGames?.AwardedSpins);
        Assert.Null(game.Energy);
        Assert.Equal(PiratesFortuneBaseReelSet.Id, game.Math.ReelSetId);
        Assert.Equal(0.32m, game.Math.Targets.HitRate);
        Assert.Same(PiratesFortuneBaseReelSet.Definition, provider.GetReelSet(game.Math.ReelSetId));
    }

    [Fact]
    public void PiratesBaseReelSet_IsDistinctFromTheClassicBaseAndContainsOnlyBaseSymbols()
    {
        var reels = PiratesFortuneBaseReelSet.Definition;

        Assert.Equal(PiratesFortuneBaseReelSet.Id, reels.Id);
        Assert.Equal(5, reels.Reels.Count);
        Assert.All(reels.Reels, reel => Assert.Equal(60, reel.Count));
        Assert.DoesNotContain(reels.Reels.SelectMany(reel => reel), symbol =>
            symbol.StartsWith("RAND_", StringComparison.Ordinal) ||
            symbol.StartsWith("SEAL_", StringComparison.Ordinal) ||
            symbol is "PAW" or "BOLT");
    }

    private static GameDefinition Prototype() => new()
    {
        Id = SlotSpecialRoundProfiles.ClassicGameId,
        Layout = new GameLayoutDefinition { ReelCount = 5, VisibleRows = 4, PaylineCount = 23 },
        Symbols = new GameSymbolRules { SymbolSetId = "symbols", WildSymbolId = "ACE" },
        Matching = new GameMatchingRules(),
        Math = new GameMathDefinition
        {
            ReelSetId = "reels",
            PaytableId = "paytable",
            PaylinePayoutSteps = Enumerable.Repeat(0, 23).ToList(),
            Targets = new GameMathTargets()
        },
        Wagering = new GameWageringDefinition(),
        FreeGames = new GameFreeGamesDefinition { SymbolId = "FREE", RequiredSymbols = 3, AwardedSpins = 5 },
        Paylines = Enumerable.Range(0, 23)
            .Select(index => Enumerable.Repeat(index % 4, 5).ToList())
            .ToList()
    };
}
