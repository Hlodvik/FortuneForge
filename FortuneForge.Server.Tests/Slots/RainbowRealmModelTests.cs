using System.Text.Json;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class RainbowRealmModelTests
{
    private const string GameId = SlotSpecialRoundProfiles.RainbowRealmGameId;

    [Fact]
    public void OwnStripsUseThirtyNinePercentRanksAndFewerRegularFruits()
    {
        var provider = new OptionsSlotsDefinitionProvider(Options.Create(ReadOptions()));
        var game = Assert.IsType<GameDefinition>(provider.GetGame(GameId));
        var set = Assert.IsType<SymbolSetDefinition>(provider.GetSymbolSet(game.Symbols.SymbolSetId));
        var reels = Assert.IsType<ReelSetDefinition>(provider.GetReelSet(game.Math.ReelSetId));
        var paytable = Assert.IsType<PaytableDefinition>(provider.GetPaytable(game.Math.PaytableId));
        Assert.Equal(RainbowRealmSlotDefinitions.SymbolSetId, set.Id);
        Assert.Equal(set.Id, reels.SymbolSetId);
        Assert.Equal(set.Id, paytable.SymbolSetId);
        Assert.Null(game.Energy);
        Assert.Equal(33, SpinService.GetSealAppearanceChance(GameId, currentEnergyBalance: 100));
        Assert.Equal(5, reels.Reels.Count);
        Assert.All(reels.Reels, reel =>
        {
            Assert.Equal(1_000, reel.Count);
            Assert.Equal(390, reel.Count(symbol => symbol is "A" or "J" or "K" or "Q"));
            Assert.InRange(reel.Count(symbol => symbol is "2" or "3" or "4" or "5" or "6" or "7"), 350, 389);
            Assert.DoesNotContain(reel, symbol => symbol == "BOLT" || symbol.StartsWith("RAND_"));
        });
        Assert.All(new[] { "J", "Q", "K", "A" }, rank =>
            Assert.Contains(paytable.Rules, rule => rule.SymbolId == rank && rule.Multiplier > 0));
    }

    [Theory]
    [InlineData(0, 2, 0)]
    [InlineData(1, 2, 18)]
    [InlineData(2, 2, 36)]
    [InlineData(1, 4, 36)]
    public void BasketHarvestUsesEveryVisibleCharmAndPaysOutsidePaylines(int baskets, int wager, int expected)
    {
        var provider = new OptionsSlotsDefinitionProvider(Options.Create(ReadOptions()));
        var game = provider.GetGame(GameId)!;
        var result = Spin(new FixedReelProvider(provider, game,
        [
            ["SEAL_SYNC", "J", "J", "J"],
            ["SEAL_ROWS", "Q", "Q", "Q"],
            ["SEAL_PAW", "K", "K", "K"],
            ["SEAL_RAND", "A", "A", baskets == 2 ? "PAW" : "A"],
            [baskets > 0 ? "PAW" : "POWER", "POWER", "POWER", "POWER"]
        ]), wager);

        Assert.Equal(baskets, result.MonkeyPawCount);
        Assert.Equal(expected, result.MoneyGrabPoints);
        Assert.Equal(expected, result.Payout.TotalPoints);
        Assert.Empty(result.Payout.Paylines);
        Assert.Equal(4, result.SealsAwarded.Count);
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));

        var altered = result with { MoneyGrabPoints = result.MoneyGrabPoints + 1 };
        Assert.Contains(SlotRoundAudit.Check(altered, altered.Payout), finding => finding.Code == "slot-feature-award-mismatch");
    }

    [Fact]
    public void KiwiModeMakesAFullWildReelWithoutValueTokens()
    {
        var provider = new OptionsSlotsDefinitionProvider(Options.Create(ReadOptions()));
        var result = Spin(provider, 2, featureMode: "rand");

        Assert.Contains(result.Reels, reel => reel.All(symbol => symbol == "ACE"));
        Assert.DoesNotContain(result.Reels.SelectMany(reel => reel), symbol => symbol.StartsWith("RAND_"));
        Assert.Empty(SlotRoundAudit.Check(result, result.Payout));
    }

    [Fact]
    public void HistoricalRainbowKeepsTokenPayoutAndSunshineWhenReplayed()
    {
        var provider = new OptionsSlotsDefinitionProvider();
        var original = Assert.IsType<GameDefinition>(provider.GetGame(GameId));
        var legacy = new GameDefinition
        {
            Id = original.Id,
            Layout = original.Layout,
            Symbols = new GameSymbolRules
            {
                SymbolSetId = "classic-symbols-v3",
                WildSymbolId = original.Symbols.WildSymbolId,
                NativeWildMatchLengths = original.Symbols.NativeWildMatchLengths,
                WildSubstitutionMatchLengths = original.Symbols.WildSubstitutionMatchLengths
            },
            Matching = original.Matching,
            Math = new GameMathDefinition
            {
                ReelSetId = "classic-reels-v4",
                PaytableId = "classic-paytable-v4",
                FiveMatchPityMissLimit = original.Math.FiveMatchPityMissLimit,
                PaylinePayoutSteps = original.Math.PaylinePayoutSteps,
                Targets = original.Math.Targets
            },
            Wagering = original.Wagering,
            FreeGames = original.FreeGames,
            SpecialPoints = original.SpecialPoints,
            Energy = new GameEnergyDefinition { SymbolId = "BOLT", PointsPerVisibleSymbol = 1 },
            Paylines = original.Paylines
        };
        var result = Spin(new FixedReelProvider(provider, legacy,
        [
            ["PAW", "SEAL_SYNC", "SEAL_SYNC", "SEAL_SYNC"],
            ["RAND_2", "SEAL_ROWS", "SEAL_ROWS", "SEAL_ROWS"],
            ["BOLT", "SEAL_PAW", "SEAL_PAW", "SEAL_PAW"],
            ["SEAL_RAND", "SEAL_RAND", "SEAL_RAND", "SEAL_RAND"],
            ["POWER", "POWER", "POWER", "POWER"]
        ]), 2, energy: 99);
        var settled = EnergyBonus.Settle(99, result.EnergyAwarded, result.Payout);

        Assert.Equal(SpinService.AuditEngineVersion, result.EngineAudit!.EngineVersion);
        Assert.Equal(4, result.MoneyGrabPoints);
        Assert.Equal(901, Assert.Single(result.Payout.Paylines).PaylineId);
        Assert.True(settled.MultiplierApplied);
        Assert.Equal(6, settled.Payout.TotalPoints);
        Assert.Empty(SlotRoundAudit.Check(result, settled.Payout));
    }

    private static SpinResult Spin(ISlotsDefinitionProvider provider, int wager, string? featureMode = null, long energy = 0)
    {
        var random = new LastChoice();
        return new SpinService(provider, new CryptoReelGenerator(random), new CombinationEvaluator(),
            new PayoutCalculator(), random).Spin(GameId, wager, "rainbow-test", false, energy, featureMode);
    }

    private static SlotsOptions ReadOptions()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        return json.RootElement.GetProperty("Slots")
            .Deserialize<SlotsOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private sealed class LastChoice : IRandomIndexSource
    {
        public int Next(int maximumExclusive) => maximumExclusive - 1;
    }

    private sealed class FixedReelProvider(OptionsSlotsDefinitionProvider definitions, GameDefinition game,
        List<List<string>> reels) : ISlotsDefinitionProvider
    {
        public GameDefinition? GetGame(string id) => id == game.Id ? game : definitions.GetGame(id);
        public SymbolSetDefinition? GetSymbolSet(string id) => definitions.GetSymbolSet(id);
        public PaytableDefinition? GetPaytable(string id) => definitions.GetPaytable(id);
        public ReelSetDefinition? GetReelSet(string id) => id == game.Math.ReelSetId
            ? new ReelSetDefinition { Id = id, SymbolSetId = game.Symbols.SymbolSetId, Reels = reels }
            : definitions.GetReelSet(id);
    }
}
