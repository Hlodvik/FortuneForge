using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class SlotPayoutFrequencyTests
{
    [Fact]
    public void FourSymbolRun_PaysTheConfiguredThreeSymbolRate()
    {
        var game = new GameDefinition
        {
            Id = "test",
            Layout = new GameLayoutDefinition
            {
                ReelCount = 5,
                VisibleRows = 1,
                PaylineCount = 1
            },
            Symbols = new GameSymbolRules
            {
                SymbolSetId = "symbols",
                WildSymbolId = "ACE",
                NativeWildMatchLengths = [3, 5],
                WildSubstitutionMatchLengths = [5]
            },
            Matching = new GameMatchingRules
            {
                MinimumRunLength = 3,
                AllowMultipleRunsPerPayline = true
            },
            Math = new GameMathDefinition
            {
                ReelSetId = "reels",
                PaytableId = "paytable",
                PaylinePayoutSteps = [0],
                Targets = new GameMathTargets()
            },
            Wagering = new GameWageringDefinition
            {
                PointValueInCents = 25,
                MinimumWagerPoints = 2,
                MaximumWagerPoints = 2,
                AllowedWagerPoints = [2]
            },
            Paylines = [[0, 0, 0, 0, 0]]
        };
        var symbolSet = new SymbolSetDefinition
        {
            Id = "symbols",
            Symbols =
            [
                new SymbolDefinition { Id = "2" },
                new SymbolDefinition { Id = "3" },
                new SymbolDefinition { Id = "ACE" }
            ]
        };
        var paytable = new PaytableDefinition
        {
            Id = "paytable",
            SymbolSetId = symbolSet.Id,
            Rules =
            [
                new PayoutRule { SymbolId = "2", MatchLength = 3, Multiplier = 1 },
                new PayoutRule { SymbolId = "2", MatchLength = 5, Multiplier = 4 }
            ]
        };
        IReadOnlyList<IReadOnlyList<string>> reels =
        [
            ["2"],
            ["2"],
            ["2"],
            ["2"],
            ["3"]
        ];

        var evaluations = new CombinationEvaluator().Evaluate(reels, game, symbolSet);
        var payout = new PayoutCalculator().Calculate(evaluations, game, paytable, 2);

        var paidMatch = Assert.Single(Assert.Single(payout.Paylines).Matches);
        Assert.Equal(4, paidMatch.Match.MatchLength);
        Assert.Equal(1, paidMatch.Multiplier);
        Assert.Equal(2, payout.TotalPoints);
    }
}
