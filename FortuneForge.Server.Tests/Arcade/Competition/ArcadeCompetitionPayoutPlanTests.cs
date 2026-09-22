using System.Collections.Immutable;
using FortuneForge.Server.Arcade.Competition;
using Xunit;

namespace FortuneForge.Server.Tests.Arcade.Competition;

public sealed class ArcadeCompetitionPayoutPlanTests
{
    [Fact]
    public void SolePlayerPlanRefundsEveryEntryFee()
    {
        var (competition, result) = Evaluate([
            Attempt("solo", 10),
            Attempt("solo", 30),
            Attempt("solo", 20),
        ]);

        var plan = ArcadeCompetitionPayoutPlan.Create(competition, result);

        var instruction = Assert.Single(plan.Instructions);
        Assert.Equal(ArcadeCompetitionPayoutKind.Refund, instruction.Kind);
        Assert.Equal("solo", instruction.PlayerId);
        Assert.Null(instruction.Placement);
        Assert.Equal(300, instruction.AmountCents);
        Assert.Equal(result.TotalEntryFeesCents, plan.TotalCreditsCents);
        Assert.Equal(0, plan.HouseCutCents);
    }

    [Fact]
    public void TwoPlayerPlanPaysTheVisibleJackpotToFirstPlace()
    {
        var (competition, result) = Evaluate([
            Attempt("alpha", 200),
            Attempt("bravo", 100),
        ]);

        var plan = ArcadeCompetitionPayoutPlan.Create(competition, result);

        var instruction = Assert.Single(plan.Instructions);
        Assert.Equal(ArcadeCompetitionPayoutKind.Prize, instruction.Kind);
        Assert.Equal("alpha", instruction.PlayerId);
        Assert.Equal(1, instruction.Placement);
        Assert.Equal(result.VisibleJackpotCents, instruction.AmountCents);
        Assert.Equal(result.HouseCutCents, result.TotalEntryFeesCents - plan.TotalCreditsCents);
    }

    [Fact]
    public void ThreePlayerPlanPaysTheVisibleJackpotAndRetainsOnlyTheHouseCut()
    {
        var (competition, result) = Evaluate([
            Attempt("alpha", 300),
            Attempt("bravo", 200),
            Attempt("charlie", 100),
        ]);

        var plan = ArcadeCompetitionPayoutPlan.Create(competition, result);

        Assert.Equal(result.VisibleJackpotCents, plan.TotalCreditsCents);
        Assert.Equal(result.HouseCutCents, result.TotalEntryFeesCents - plan.TotalCreditsCents);
        Assert.All(plan.Instructions, instruction => Assert.True(instruction.AmountCents > 0));
    }

    [Fact]
    public void MultiWinnerPlanIsDeterministicAndIssuesOnePositiveCreditPerWinner()
    {
        var (competition, result) = Evaluate([
            Attempt("echo", 100),
            Attempt("delta", 500),
            Attempt("charlie", 300),
            Attempt("bravo", 400),
            Attempt("alpha", 200),
        ]);

        var first = ArcadeCompetitionPayoutPlan.Create(competition, result);
        var second = ArcadeCompetitionPayoutPlan.Create(competition, result);

        Assert.Equal(2, first.Instructions.Length);
        Assert.True(first.Instructions.SequenceEqual(second.Instructions));
        Assert.Equal(new int?[] { 1, 2 }, first.Instructions.Select(instruction => instruction.Placement));
        Assert.Equal(new[] { "delta", "bravo" }, first.Instructions.Select(instruction => instruction.PlayerId));
        Assert.All(first.Instructions, instruction =>
        {
            Assert.Equal(ArcadeCompetitionPayoutKind.Prize, instruction.Kind);
            Assert.True(instruction.AmountCents > 0);
            Assert.StartsWith("arcade-competition-credit-v1-", instruction.IdempotencyKey, StringComparison.Ordinal);
        });
        Assert.Equal(
            first.Instructions.Length,
            first.Instructions.Select(instruction => instruction.IdempotencyKey).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(result.VisibleJackpotCents, first.TotalCreditsCents);
        Assert.Equal(result.HouseCutCents, result.TotalEntryFeesCents - first.TotalCreditsCents);
    }

    [Fact]
    public void PlanRejectsNonPositiveAndDuplicatePlayerInstructions()
    {
        var (competition, result) = Evaluate([
            Attempt("alpha", 500),
            Attempt("bravo", 400),
            Attempt("charlie", 300),
            Attempt("delta", 200),
            Attempt("echo", 100),
        ]);
        var nonPositive = result with
        {
            PrizeAllocations = result.PrizeAllocations.SetItem(
                0,
                result.PrizeAllocations[0] with { AmountCents = 0 })
        };
        var duplicatePlayer = result with
        {
            PrizeAllocations = result.PrizeAllocations.SetItem(
                1,
                result.PrizeAllocations[1] with { PlayerId = result.PrizeAllocations[0].PlayerId })
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ArcadeCompetitionPayoutPlan.Create(competition, nonPositive));
        Assert.Throws<ArgumentException>(() =>
            ArcadeCompetitionPayoutPlan.Create(competition, duplicatePlayer));
    }

    [Fact]
    public void PlanRejectsIncompleteRefundsAndMultiplayerJackpotAllocations()
    {
        var (soleCompetition, soleResult) = Evaluate([
            Attempt("solo", 2),
            Attempt("solo", 1),
        ]);
        var incompleteRefund = soleResult with
        {
            Refunds = ImmutableArray.Create(soleResult.Refunds[0] with
            {
                AmountCents = soleResult.TotalEntryFeesCents - 1
            })
        };
        var (multiplayerCompetition, multiplayerResult) = Evaluate([
            Attempt("alpha", 2),
            Attempt("bravo", 1),
        ]);
        var incompletePrize = multiplayerResult with
        {
            PrizeAllocations = ImmutableArray.Create(multiplayerResult.PrizeAllocations[0] with
            {
                AmountCents = multiplayerResult.VisibleJackpotCents - 1
            })
        };

        Assert.Throws<ArgumentException>(() =>
            ArcadeCompetitionPayoutPlan.Create(soleCompetition, incompleteRefund));
        Assert.Throws<ArgumentException>(() =>
            ArcadeCompetitionPayoutPlan.Create(multiplayerCompetition, incompletePrize));
    }

    private static (ArcadeCompetitionIdentity Competition, ArcadeCompetitionRulesResult Result) Evaluate(
        IEnumerable<ArcadeCompetitionAttempt> attempts)
    {
        var window = ArcadeCompetitionRules.GetWindow(
            ArcadeCompetitionWindowKind.Daily,
            new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero));
        var competition = new ArcadeCompetitionIdentity(
            "asteroids",
            window.Kind,
            window.StartsAtUtc,
            window.EndsAtUtc);
        return (competition, ArcadeCompetitionRules.Evaluate(window, attempts));
    }

    private static ArcadeCompetitionAttempt Attempt(string playerId, long score) => new(playerId, score);
}
