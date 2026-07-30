using FortuneForge.Server.Payments;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Reels;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class FreeGameFrequencyTests
{
    [Fact]
    public void Apply_WithDivisorTwo_KeepsOneHalfOfFreeSymbolsForAlternatingRolls()
    {
        var outcome = new ReelOutcome(
            [0],
            [["FREE", "FREE", "FREE", "FREE"]]);
        var result = FreeGameFrequency.Apply(
            outcome,
            Game(visibleFrequencyDivisor: 2),
            new QueuedRandomIndexSource(0, 1, 0, 0, 1, 0));

        Assert.Equal(["FREE", "2", "FREE", "2"], result.VisibleReels[0]);
    }

    [Fact]
    public void PaymentCatalog_UsesOneRandPerStoredBalanceUnit()
    {
        Assert.All(PaymentCatalog.Markets, market =>
            Assert.Equal(1, market.CreditsPerCurrencyUnit));
    }

    private static GameDefinition Game(int visibleFrequencyDivisor) => new()
    {
        Id = "frequency-test",
        Layout = new GameLayoutDefinition { ReelCount = 1, VisibleRows = 4, PaylineCount = 1 },
        Symbols = new GameSymbolRules { SymbolSetId = "symbols", WildSymbolId = "ACE" },
        Matching = new GameMatchingRules { MinimumRunLength = 1 },
        Math = new GameMathDefinition
        {
            ReelSetId = "reels",
            PaytableId = "paytable",
            Targets = new GameMathTargets()
        },
        Wagering = new GameWageringDefinition
        {
            PointValueInCents = 100,
            MinimumWagerPoints = 1,
            AllowedWagerPoints = [1]
        },
        FreeGames = new GameFreeGamesDefinition
        {
            SymbolId = "FREE",
            RequiredSymbols = 3,
            AwardedSpins = 5,
            VisibleFrequencyDivisor = visibleFrequencyDivisor
        },
        SpecialPoints = new GameSpecialPointsDefinition
        {
            SymbolId = "POWER",
            ThreeMatchPoints = 1,
            FiveMatchPoints = 2,
            ActivationCost = 1,
            CommonSymbolIds = ["2"]
        }
    };

    private sealed class QueuedRandomIndexSource(params int[] values) : IRandomIndexSource
    {
        private readonly Queue<int> _values = new(values);

        public int Next(int maximumExclusive) =>
            _values.Count == 0
                ? 0
                : Math.Clamp(_values.Dequeue(), 0, maximumExclusive - 1);
    }
}
