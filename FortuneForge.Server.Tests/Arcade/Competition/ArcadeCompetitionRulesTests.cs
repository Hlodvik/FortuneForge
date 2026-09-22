using FortuneForge.Server.Arcade.Competition;
using System.Collections.Immutable;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

public sealed class ArcadeCompetitionRulesTests
{
    [Fact]
    public void DailyWindowResetsAtJohannesburgMidnight()
    {
        var beforeMidnight = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, new DateTimeOffset(2026, 9, 3, 21, 59, 59, TimeSpan.Zero));
        var atMidnight = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, new DateTimeOffset(2026, 9, 3, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 9, 2, 22, 0, 0, TimeSpan.Zero), beforeMidnight.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 3, 22, 0, 0, TimeSpan.Zero), beforeMidnight.EndsAtUtc);
        Assert.Equal(beforeMidnight.EndsAtUtc, atMidnight.StartsAtUtc);
    }

    [Fact]
    public void WeeklyWindowResetsAtJohannesburgMondayMidnight()
    {
        var beforeMonday = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Weekly, new DateTimeOffset(2026, 9, 6, 21, 59, 59, TimeSpan.Zero));
        var atMondayMidnight = ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Weekly, new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 8, 30, 22, 0, 0, TimeSpan.Zero), beforeMonday.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero), beforeMonday.EndsAtUtc);
        Assert.Equal(beforeMonday.EndsAtUtc, atMondayMidnight.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 13, 22, 0, 0, TimeSpan.Zero), atMondayMidnight.EndsAtUtc);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(9, 2)]
    [InlineData(10, 3)]
    [InlineData(99, 3)]
    [InlineData(100, 10)]
    [InlineData(999, 10)]
    [InlineData(1000, 20)]
    public void PlacementTiersUseUniquePlayerCounts(int uniquePlayerCount, int expectedPlacements) =>
        Assert.Equal(expectedPlacements, ArcadeCompetitionRules.PrizePlacementCountFor(uniquePlayerCount));

    [Fact]
    public void OnePlayerReceivesARefundForEveryAttemptAndNoPrizePlacement()
    {
        var result = Evaluate([Attempt("player-1", 10), Attempt("player-1", 25), Attempt("player-1", 5)]);

        Assert.Equal(100, result.EntryFeeCents);
        Assert.Equal(300, result.TotalEntryFeesCents);
        Assert.Empty(result.PrizePlacements);
        Assert.Empty(result.PrizeAllocations);
        var refund = Assert.Single(result.Refunds);
        Assert.Equal("player-1", refund.PlayerId);
        Assert.Equal(300, refund.AmountCents);
    }

    [Fact]
    public void RankingUsesEachPlayersBestScoreAndPlayerIdBreaksScoreTies()
    {
        var result = Evaluate([
            Attempt("bravo", 300), Attempt("alpha", 100), Attempt("alpha", 300), Attempt("charlie", 200),
        ]);

        var placement = Assert.Single(result.PrizePlacements);
        Assert.Equal("alpha", placement.PlayerId);
        Assert.Equal(300, placement.Score);
        Assert.Equal(3, result.UniquePlayerCount);
    }

    [Fact]
    public void IncompleteEntriesRemainInFeeAndPlayerAccountingAndRankAsZero()
    {
        var result = Evaluate([
            new ArcadeCompetitionAttempt("alpha", null),
            Attempt("bravo", 5),
        ]);

        Assert.Equal(2, result.AttemptCount);
        Assert.Equal(2, result.UniquePlayerCount);
        Assert.Equal(200, result.TotalEntryFeesCents);
        var placement = Assert.Single(result.PrizePlacements);
        Assert.Equal("bravo", placement.PlayerId);
        Assert.Equal(5, placement.Score);
    }

    [Fact]
    public void HouseCutStartsAtZeroRisesMonotonicallyAndCapsAtSixPercent()
    {
        var basisPoints = Enumerable.Range(1, 30).Select(playerCount => ArcadeCompetitionRules.HouseCutBasisPointsFor(playerCount)).ToArray();

        Assert.Equal(0, basisPoints[0]);
        Assert.Equal(0, basisPoints[1]);
        Assert.Equal(600, basisPoints[14]);
        Assert.All(basisPoints.Skip(14), cut => Assert.Equal(600, cut));
        Assert.True(basisPoints.Zip(basisPoints.Skip(1)).All(pair => pair.Second >= pair.First));
    }

    [Fact]
    public void VisibleJackpotEqualsEntryFeesLessTheHouseCut()
    {
        var result = Evaluate(Enumerable.Range(1, 15).Select(index => Attempt($"player-{index}", index)));

        Assert.Equal(1_500, result.TotalEntryFeesCents);
        Assert.Equal(600, result.HouseCutBasisPoints);
        Assert.Equal(90, result.HouseCutCents);
        Assert.Equal(1_410, result.VisibleJackpotCents);
    }

    [Theory]
    [InlineData(2, new[] { 10_000 })]
    [InlineData(5, new[] { 7_000, 3_000 })]
    [InlineData(10, new[] { 5_000, 3_000, 2_000 })]
    [InlineData(100, new[] { 3_000, 1_800, 1_300, 1_000, 800, 600, 500, 400, 300, 300 })]
    [InlineData(1000, new[] { 2_200, 1_200, 900, 700, 600, 500, 500, 400, 400, 400, 300, 300, 300, 300, 200, 200, 200, 200, 100, 100 })]
    public void DefaultPrizeSchedulesMatchEveryPlacementTier(int uniquePlayerCount, int[] expectedBasisPoints)
    {
        var result = Evaluate(Enumerable.Range(1, uniquePlayerCount).Select(index => Attempt($"player-{index:D4}", index)));

        Assert.Equal(expectedBasisPoints.Length, result.PrizeAllocations.Length);
        Assert.Equal(expectedBasisPoints, ArcadeCompetitionRules.DefaultPrizeSchedules[result.PrizeAllocations.Length]);
        Assert.Equal(result.PrizePlacements.Select(placement => placement.PlayerId), result.PrizeAllocations.Select(allocation => allocation.PlayerId));
    }

    [Fact]
    public void FractionalCentSharesAllocateTheEntireVisibleJackpot()
    {
        var result = Evaluate(Attempts(10), new ArcadeCompetitionRulesOptions { EntryFeeCents = 7 });

        Assert.Equal(68, result.VisibleJackpotCents);
        Assert.Equal(new long[] { 35, 20, 13 }, result.PrizeAllocations.Select(allocation => allocation.AmountCents));
        Assert.Equal(result.VisibleJackpotCents, result.PrizeAllocations.Sum(allocation => allocation.AmountCents));
    }

    [Fact]
    public void RoundingRemaindersAreAwardedInRankOrder()
    {
        var schedules = ArcadeCompetitionRules.DefaultPrizeSchedules.SetItem(3, ImmutableArray.Create(3_333, 3_333, 3_334));
        var result = Evaluate(
            Attempts(10),
            new ArcadeCompetitionRulesOptions { EntryFeeCents = 1, PrizeSchedules = schedules });

        Assert.Equal(new long[] { 4, 3, 3 }, result.PrizeAllocations.Select(allocation => allocation.AmountCents));
    }

    [Fact]
    public void InvalidPrizeSchedulesAreRejected()
    {
        var schedules = ArcadeCompetitionRules.DefaultPrizeSchedules.SetItem(2, ImmutableArray.Create(10_000));

        Assert.Throws<ArgumentException>(() => Evaluate(
            [Attempt("alpha", 2), Attempt("bravo", 1)],
            new ArcadeCompetitionRulesOptions { PrizeSchedules = schedules }));
    }

    private static ArcadeCompetitionRulesResult Evaluate(IEnumerable<ArcadeCompetitionAttempt> attempts, ArcadeCompetitionRulesOptions? options = null) =>
        ArcadeCompetitionRules.Evaluate(
            ArcadeCompetitionRules.GetWindow(ArcadeCompetitionWindowKind.Daily, new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero)),
            attempts,
            options);

    private static ArcadeCompetitionAttempt Attempt(string playerId, long score) => new(playerId, score);

    private static IEnumerable<ArcadeCompetitionAttempt> Attempts(int count) =>
        Enumerable.Range(1, count).Select(index => Attempt($"player-{index:D4}", index));
}
