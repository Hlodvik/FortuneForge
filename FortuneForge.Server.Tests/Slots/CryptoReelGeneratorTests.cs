using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Reels;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class CryptoReelGeneratorTests
{
    [Fact]
    public void Generate_WithoutAnEnergyFeature_ExcludesTheEnergySymbol()
    {
        var outcome = new CryptoReelGenerator(new FixedRandomIndexSource()).Generate(
            Game(),
            new ReelSetDefinition
            {
                Id = "reels",
                SymbolSetId = "symbols",
                Reels = [["BOLT", "2", "3", "4", "5"]]
            },
            new SymbolSetDefinition
            {
                Id = "symbols",
                Symbols = [new SymbolDefinition { Id = "BOLT" }, new SymbolDefinition { Id = "2" },
                    new SymbolDefinition { Id = "3" }, new SymbolDefinition { Id = "4" }, new SymbolDefinition { Id = "5" }]
            });

        Assert.DoesNotContain("BOLT", outcome.VisibleReels.Single());
    }

    private static GameDefinition Game() => new()
    {
        Id = "without-energy",
        Layout = new GameLayoutDefinition { ReelCount = 1, VisibleRows = 4 },
        Symbols = new GameSymbolRules { SymbolSetId = "symbols", WildSymbolId = "ACE" },
        Matching = new GameMatchingRules(),
        Math = new GameMathDefinition { ReelSetId = "reels", PaytableId = "paytable", Targets = new GameMathTargets() },
        Wagering = new GameWageringDefinition()
    };

    private sealed class FixedRandomIndexSource : IRandomIndexSource
    {
        public int Next(int exclusiveMax) => 0;
    }
}
