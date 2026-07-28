using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class SlotFeatureSpinTests
{
    [Fact]
    public void Spin_WhenMonkeyPawAndRandMultiplierAreVisible_GrabsMultiplierValue()
    {
        var random = new QueuedRandomIndexSource(20, 0, 80, 1, 0, 0, 0, 99);
        var service = CreateService(random);

        var result = service.Spin("classic-demo-v1", 100, "player", specialBoostApplied: false);

        Assert.Equal(1, result.MonkeyPawCount);
        Assert.Equal(200, result.MoneyGrabPoints);
        Assert.Equal(200, result.Payout.TotalPoints);
        Assert.Contains(result.Payout.Paylines, payout => payout.PaylineId == 901);
    }

    [Fact]
    public void Spin_WhenThreeBananasLandVertically_PaysThreeTimesWager()
    {
        var random = new QueuedRandomIndexSource(0, 1, 1, 99, 0, 0, 0, 99);
        var service = CreateService(random);

        var result = service.Spin("classic-demo-v1", 100, "player", specialBoostApplied: false);

        Assert.Equal(300, result.BananaBonusPoints);
        Assert.Equal(300, result.Payout.TotalPoints);
        Assert.Contains(result.Payout.Paylines, payout => payout.PaylineId >= 801 && payout.PaylineId < 900);
    }

    [Fact]
    public void Spin_WhenRowsFeatureModeIsActive_ReturnsTwoExtraVisibleRows()
    {
        var random = new QueuedRandomIndexSource(0, 1, 1, 0, 99);
        var service = CreateService(random);

        var result = service.Spin(
            "classic-demo-v1",
            100,
            "player",
            specialBoostApplied: false,
            freeSpinFeatureMode: "rows");

        Assert.All(result.Reels, reel => Assert.Equal(6, reel.Count));
    }

    [Fact]
    public void Spin_WhenSealAppearsAnywhereVisible_CountsThatSeal()
    {
        var random = new QueuedRandomIndexSource(0, 1, 1, 0, 0, 1, 0, 2);
        var service = CreateService(random);

        var result = service.Spin(
            "classic-demo-v1",
            100,
            "player",
            specialBoostApplied: false,
            currentEnergyBalance: 75);

        Assert.True(result.SealsAwarded.TryGetValue("SEAL_PAW", out var count));
        Assert.Equal(1, count);
    }

    private static SpinService CreateService(IRandomIndexSource random) =>
        new(
            new TestSlotsDefinitionProvider(),
            new CommonSymbolReelGenerator(),
            new NoWinningCombinationEvaluator(),
            new ZeroPayoutCalculator(),
            random);

    private sealed class TestSlotsDefinitionProvider : ISlotsDefinitionProvider
    {
        private static readonly SymbolSetDefinition SymbolSet = new()
        {
            Id = "wukong-treasures-v3",
            Symbols =
            [
                new SymbolDefinition { Id = "2" },
                new SymbolDefinition { Id = "3" },
                new SymbolDefinition { Id = "4" },
                new SymbolDefinition { Id = "5" },
                new SymbolDefinition { Id = "6" },
                new SymbolDefinition { Id = "7" },
                new SymbolDefinition { Id = "ACE" },
                new SymbolDefinition { Id = "FREE" },
                new SymbolDefinition { Id = "POWER" },
                new SymbolDefinition { Id = "BOLT" },
                new SymbolDefinition { Id = "BANANA" },
                new SymbolDefinition { Id = "PAW" },
                new SymbolDefinition { Id = "RAND_05" },
                new SymbolDefinition { Id = "RAND_1" },
                new SymbolDefinition { Id = "RAND_15" },
                new SymbolDefinition { Id = "RAND_2" },
                new SymbolDefinition { Id = "RAND_3" },
                new SymbolDefinition { Id = "RAND_4" },
                new SymbolDefinition { Id = "RAND_5" },
                new SymbolDefinition { Id = "SEAL_SYNC" },
                new SymbolDefinition { Id = "SEAL_ROWS" },
                new SymbolDefinition { Id = "SEAL_PAW" },
                new SymbolDefinition { Id = "SEAL_RAND" }
            ]
        };

        private static readonly GameDefinition Game = new()
        {
            Id = "classic-demo-v1",
            Layout = new GameLayoutDefinition
            {
                ReelCount = 5,
                VisibleRows = 4,
                PaylineCount = 1
            },
            Symbols = new GameSymbolRules
            {
                SymbolSetId = SymbolSet.Id,
                WildSymbolId = "ACE"
            },
            Matching = new GameMatchingRules
            {
                MinimumRunLength = 3,
                AllowMultipleRunsPerPayline = true
            },
            Math = new GameMathDefinition
            {
                ReelSetId = "test-reels",
                PaytableId = "test-paytable",
                Targets = new GameMathTargets()
            },
            Wagering = new GameWageringDefinition
            {
                PointValueInCents = 1,
                MinimumWagerPoints = 50,
                MaximumWagerPoints = 500,
                AllowedWagerPoints = [50, 100, 250, 500]
            },
            FreeGames = new GameFreeGamesDefinition
            {
                SymbolId = "FREE",
                RequiredSymbols = 3,
                AwardedSpins = 5
            },
            SpecialPoints = new GameSpecialPointsDefinition
            {
                SymbolId = "POWER",
                ThreeMatchPoints = 1,
                FiveMatchPoints = 2,
                ActivationCost = 8,
                CommonSymbolIds = ["2", "3", "4"]
            },
            Energy = new GameEnergyDefinition
            {
                SymbolId = "BOLT",
                PointsPerVisibleSymbol = 1
            },
            Paylines = [[0, 0, 0, 0, 0]]
        };

        private static readonly ReelSetDefinition ReelSet = new()
        {
            Id = "test-reels",
            SymbolSetId = SymbolSet.Id,
            Reels =
            [
                ["2", "2", "2", "2"],
                ["2", "2", "2", "2"],
                ["2", "2", "2", "2"],
                ["2", "2", "2", "2"],
                ["2", "2", "2", "2"]
            ]
        };

        private static readonly PaytableDefinition Paytable = new()
        {
            Id = "test-paytable",
            SymbolSetId = SymbolSet.Id
        };

        public GameDefinition? GetGame(string id) =>
            string.Equals(id, Game.Id, StringComparison.Ordinal) ? Game : null;

        public SymbolSetDefinition? GetSymbolSet(string id) =>
            string.Equals(id, SymbolSet.Id, StringComparison.Ordinal) ? SymbolSet : null;

        public ReelSetDefinition? GetReelSet(string id) =>
            string.Equals(id, ReelSet.Id, StringComparison.Ordinal) ? ReelSet : null;

        public PaytableDefinition? GetPaytable(string id) =>
            string.Equals(id, Paytable.Id, StringComparison.Ordinal) ? Paytable : null;
    }

    private sealed class CommonSymbolReelGenerator : IReelGenerator
    {
        public ReelOutcome Generate(
            GameDefinition game,
            ReelSetDefinition reelSet,
            SymbolSetDefinition symbolSet) =>
            new(
                Enumerable.Repeat(0, game.Layout.ReelCount).ToArray(),
                Enumerable.Range(0, game.Layout.ReelCount)
                    .Select(_ => (IReadOnlyList<string>)Enumerable.Repeat("2", game.Layout.VisibleRows).ToArray())
                    .ToArray());
    }

    private sealed class NoWinningCombinationEvaluator : ICombinationEvaluator
    {
        public IReadOnlyList<PaylineEvaluation> Evaluate(
            IReadOnlyList<IReadOnlyList<string>> reels,
            GameDefinition game,
            SymbolSetDefinition symbolSet) => [];
    }

    private sealed class ZeroPayoutCalculator : IPayoutCalculator
    {
        public SpinPayout Calculate(
            IReadOnlyList<PaylineEvaluation> evaluations,
            GameDefinition game,
            PaytableDefinition paytable,
            long wagerPoints) => new(0, []);
    }

    private sealed class QueuedRandomIndexSource(params int[] values) : IRandomIndexSource
    {
        private readonly Queue<int> _values = new(values);

        public int Next(int maximumExclusive)
        {
            if (_values.Count == 0)
            {
                return Math.Max(0, maximumExclusive - 1);
            }

            var value = _values.Dequeue();
            return maximumExclusive <= 0 ? 0 : Math.Clamp(value, 0, maximumExclusive - 1);
        }
    }
}
