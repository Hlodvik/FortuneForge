using FortuneForge.Server.Slots.Bonuses;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

public sealed class MidnightVaultBonusRoundPropertiesTests
{
    private const string V1 = "midnight-vault-paytable-v1";
    private const string V2 = "midnight-vault-paytable-v2";
    private const string V3 = "midnight-vault-paytable-v3";

    [Theory]
    [InlineData(V2)]
    [InlineData(V3)]
    public void MixedCashStakeGemSequencesLockTheRoundedArithmeticMeanOfAllThirtyGems(string model)
    {
        var random = new Random(82319);
        for (var trial = 0; trial < 128; trial++)
        {
            var gemStakes = Enumerable.Range(0, 30).Select(_ => 2L * (1 + random.Next(1000))).ToArray();
            var expected = (long)Math.Round(gemStakes.Sum() / 30m, MidpointRounding.AwayFromZero);
            var counts = Tracks(0);
            var totals = Tracks(0);
            IReadOnlyList<MidnightVaultBonusRound> rounds = [];
            for (var gem = 0; gem < gemStakes.Length; gem++)
            {
                var progress = Advance(rounds, counts, totals, 1, gemStakes[gem], 1 + trial * 30 + gem, model);
                rounds = progress.Rounds;
                counts = progress.CollectionCounts;
                totals = progress.CollectionWagerPoints;
            }
            var round = Assert.Single(rounds);
            Assert.Equal(expected, round.WagerPoints);
            Assert.Equal(10, round.TotalSpins);
            Assert.Equal(10, round.RemainingSpins);
            Assert.Equal(0, counts["sync"]);
            Assert.Equal(0, totals["sync"]);
        }
    }

    [Theory]
    [InlineData(58, 16, 2)]
    [InlineData(58, 17, 3)]
    [InlineData(58000, 2, 1933)]
    [InlineData(58000, 2000, 2000)]
    public void CompletionRoundsOnlyOnceIncludingTheHalfPointBoundary(long priorTotal, long finalStake, long expected)
    {
        var progress = Advance([], Tracks(29), Tracks(priorTotal), 1, finalStake, 4000);
        Assert.Equal(expected, Assert.Single(progress.AwardedRounds).WagerPoints);
    }

    [Fact]
    public void OverflowGemsKeepTheirOwnStakeAndCannotBeRepricedAtTheCompletedPilesAverage()
    {
        var first = Advance([], Tracks(29), Tracks(58), 3, 100, 5000);
        Assert.Equal(5, Assert.Single(first.AwardedRounds).WagerPoints);
        Assert.Equal(2, first.CollectionCounts["sync"]);
        Assert.Equal(200, first.CollectionWagerPoints["sync"]);
        var second = Advance(first.Rounds, first.CollectionCounts, first.CollectionWagerPoints, 28, 2000, 5001);
        Assert.Equal(1873, Assert.Single(second.AwardedRounds).WagerPoints);
        Assert.Equal(new long[] { 5, 1873 }, second.Rounds.Select(round => round.WagerPoints));
        Assert.Equal(first.Rounds[0], second.Rounds[0]);
        Assert.Equal(0, second.CollectionCounts["sync"]);
        Assert.Equal(0, second.CollectionWagerPoints["sync"]);
    }

    [Fact]
    public void PartitioningSameStakeGemArrivalsPreservesRoundWagersAndOrder()
    {
        var oneBatch = Advance([], Tracks(29), Tracks(58), 61, 100, 6000);
        IReadOnlyList<MidnightVaultBonusRound> splitRounds = [];
        var counts = Tracks(29);
        var totals = Tracks(58);
        var id = 6001;
        foreach (var incoming in new[] { 1, 7, 23, 30 })
        {
            var progress = Advance(splitRounds, counts, totals, incoming, 100, id++);
            splitRounds = progress.Rounds;
            counts = progress.CollectionCounts;
            totals = progress.CollectionWagerPoints;
        }
        Assert.Equal(new long[] { 5, 100, 100 }, oneBatch.Rounds.Select(round => round.WagerPoints));
        Assert.Equal(oneBatch.Rounds.Select(round => (round.TotalSpins, round.RemainingSpins, round.WagerPoints, round.FeatureMode, round.MathModelId)),
            splitRounds.Select(round => (round.TotalSpins, round.RemainingSpins, round.WagerPoints, round.FeatureMode, round.MathModelId)));
        Assert.Equal(oneBatch.CollectionCounts["sync"], counts["sync"]);
        Assert.Equal(oneBatch.CollectionWagerPoints["sync"], totals["sync"]);
        Assert.Equal(3, splitRounds.Select(round => round.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ConsumptionChangesOnlyTheHeadRemainderAndPreservesQueuedWagerModeAndModel()
    {
        var active = new MidnightVaultBonusRound("active", 10, 2, 3, "rows-paw", V2);
        var queued = new MidnightVaultBonusRound("legacy-next", 6, 6, 2000, null, V1);
        var before = new[] { active, queued };
        var first = MidnightVaultBonusRounds.Consume(before);
        Assert.Equal(active with { RemainingSpins = 1 }, first[0]);
        Assert.Equal(queued, first[1]);
        Assert.Equal(2, before[0].RemainingSpins);
        var second = MidnightVaultBonusRounds.Consume(first);
        Assert.Equal(queued, Assert.Single(second));
        var third = MidnightVaultBonusRounds.Consume(second);
        Assert.Equal(queued with { RemainingSpins = 5 }, Assert.Single(third));
    }

    private static IReadOnlyDictionary<string, long> Tracks(long sync) =>
        new Dictionary<string, long> { ["sync"] = sync, ["rows"] = 0, ["paw"] = 0, ["rand"] = 0 };

    private static MidnightVaultBonusProgress Advance(IReadOnlyList<MidnightVaultBonusRound> rounds,
        IReadOnlyDictionary<string, long> counts, IReadOnlyDictionary<string, long> totals, int incoming, long stake, int id, string model = V3) =>
        MidnightVaultBonusRounds.Advance(rounds, counts, totals, new Dictionary<string, int> { ["SEAL_SYNC"] = incoming },
            stake, 0, null, model, new Guid(id, 0, 0, new byte[8]));
}
