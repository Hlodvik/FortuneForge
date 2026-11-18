using FortuneForge.Games.NeonNights;
using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.NeonNights.Tests;

public sealed class NeonDiscoClusterTests
{
    private static readonly GameDefinition Game = NeonDiscoDefinitions.CreateGame(NeonArcadeSlotDefinitions.CreateGame());
    private static readonly PaytableDefinition Paytable = NeonDiscoDefinitions.CreatePaytable();

    [Fact]
    public void DiscoWildConnectsAnOrthogonalClusterAndReportsItsSubstitution()
    {
        var board = NonWinningBoard();
        board[0][0] = "2";
        board[1][0] = NeonDiscoDefinitions.WildSymbolId;
        board[2][0] = "2";
        board[2][1] = "2";
        var payout = NeonDiscoCascade.EvaluateClusters(board, Game, Paytable, 100, 1);
        var match = Assert.Single(Assert.Single(payout.Paylines).Matches);
        Assert.Equal("2", match.Match.SymbolId);
        Assert.Equal(4, match.Match.MatchLength);
        Assert.Equal(new[] { new GridPosition(1, 0) }, match.Match.WildPositions);
        Assert.Equal(137, match.AmountPoints);

        board[1][0] = "5";
        board[1][1] = NeonDiscoDefinitions.WildSymbolId;
        board[2][1] = "7";
        Assert.Empty(NeonDiscoCascade.EvaluateClusters(board, Game, Paytable, 100, 1).Paylines);
    }

    [Fact]
    public void CompetingClustersUseEachWildOnceAndRetainOtherPayingGroups()
    {
        var board = NonWinningBoard();
        board[0][0] = board[1][0] = board[1][1] = "2";
        board[2][0] = NeonDiscoDefinitions.WildSymbolId;
        board[3][0] = board[4][0] = board[3][1] = "DISCO_BALL";
        board[3][2] = "5";
        board[4][1] = "4";
        var payout = NeonDiscoCascade.EvaluateClusters(board, Game, Paytable, 100, 1);
        var match = Assert.Single(Assert.Single(payout.Paylines).Matches);
        Assert.Equal("DISCO_BALL", match.Match.SymbolId);
        Assert.Equal(new[] { new GridPosition(2, 0) }, match.Match.WildPositions);

        board[0][1] = "2";
        payout = NeonDiscoCascade.EvaluateClusters(board, Game, Paytable, 100, 1);
        Assert.Equal(2, payout.Paylines.Count);
        Assert.Equal(new[] { "DISCO_BALL", "2" }, payout.Paylines.Select(line => line.Matches[0].Match.SymbolId));
        var cells = payout.Paylines.SelectMany(line => line.Matches[0].Match.Positions).ToArray();
        Assert.Equal(cells.Length, cells.Distinct().Count());
        Assert.Empty(payout.Paylines[1].Matches[0].Match.WildPositions);
    }

    [Fact]
    public void WildsHaveNoNativePrizeAndWildOnlyGroupsRemainOnTheBoard()
    {
        var board = Enumerable.Range(0, 5)
            .Select(_ => Enumerable.Repeat(NeonDiscoDefinitions.WildSymbolId, 4).ToArray()).ToArray();
        Assert.DoesNotContain(Paytable.Rules, rule => rule.SymbolId == NeonDiscoDefinitions.WildSymbolId);
        Assert.Empty(NeonDiscoCascade.EvaluateClusters(board, Game, Paytable, 100, 1).Paylines);
        var result = Resolve(board, Reels(NeonDiscoDefinitions.AllSymbolIds),
            _ => throw new InvalidOperationException("A non-paying WILD group must not draw refill symbols."));
        Assert.Empty(result.Steps);
        Assert.Equal(0, result.TotalPoints);
    }

    [Fact]
    public void WinningWildClearsOnceAndRefillsWithTheCluster()
    {
        var board = NonWinningBoard();
        board[0][2] = board[0][3] = board[1][2] = "2";
        board[1][3] = NeonDiscoDefinitions.WildSymbolId;
        var draws = new Queue<int>([4, 5, 6, 4]);
        var result = Resolve(board, Reels(NeonDiscoDefinitions.AllSymbolIds), _ => draws.Dequeue());
        var step = Assert.Single(result.Steps);
        Assert.Equal(4, step.Cleared.Count);
        Assert.Contains(new GridPosition(1, 3), step.Cleared);
        Assert.Single(Assert.Single(Assert.Single(step.Payout.Paylines).Matches).Match.WildPositions);
        Assert.Empty(draws);
    }

    [Fact]
    public void WildCascadeOutcomesReplayAndRejectChangedSubstitutionEvidence()
    {
        var board = NonWinningBoard();
        board[0][2] = board[0][3] = board[1][2] = "2";
        board[1][3] = NeonDiscoDefinitions.WildSymbolId;
        var outcome = new NeonNightsEngine(new DiscoFixtureDefinitions(board), new FixtureRandom())
            .Spin(100, "disco-wild", false, 0, null, 0, false, true);
        Assert.Equal(NeonArcadeSlotDefinitions.EngineVersion, outcome.EngineAudit.EngineVersion);
        Assert.Equal(NeonDiscoDefinitions.SymbolSetId, outcome.SymbolSetId);
        Assert.Contains(outcome.NeonDiscoCascade!.Steps.SelectMany(step => step.Payout.Paylines)
            .SelectMany(line => line.Matches), match => match.Match.WildPositions.Count > 0);
        Assert.Empty(NeonOutcomeReplay.Check(outcome, outcome.Payout));

        var steps = outcome.NeonDiscoCascade.Steps.ToArray();
        var lines = steps[0].Payout.Paylines.ToArray();
        var paid = Assert.Single(lines[0].Matches);
        lines[0] = lines[0] with { Matches = [paid with { Match = paid.Match with { WildPositions = [] } }] };
        steps[0] = steps[0] with { Payout = steps[0].Payout with { Paylines = lines } };
        var changed = outcome with { NeonDiscoCascade = outcome.NeonDiscoCascade with { Steps = steps } };
        Assert.Contains(NeonOutcomeReplay.Check(changed, changed.Payout),
            finding => finding.Code == "slot-neon-disco-steps-mismatch");
    }

    [Fact]
    public void FourMatchingSymbolsCanFormAnLButDiagonalMatchesDoNotConnect()
    {
        var board = NonWinningBoard();
        foreach (var cell in new GridPosition[] { new(0, 0), new(0, 1), new(0, 2), new(1, 2) })
            board[cell.Reel][cell.Row] = "2";
        var payout = NeonDiscoCascade.EvaluateClusters(board, Game, Paytable, 100, 1);
        var match = Assert.Single(Assert.Single(payout.Paylines).Matches);
        Assert.Equal(4, match.Match.MatchLength);
        Assert.Equal(137, match.AmountPoints);

        board = NonWinningBoard();
        for (var index = 0; index < 4; index++) board[index][index] = "2";
        Assert.Empty(NeonDiscoCascade.EvaluateClusters(board, Game, Paytable, 100, 1).Paylines);
    }

    [Fact]
    public void DisconnectedSquaresOfTheSameSymbolReceiveSeparateAwards()
    {
        var board = NonWinningBoard();
        foreach (var cell in new GridPosition[]
        {
            new(0, 0), new(0, 1), new(1, 0), new(1, 1),
            new(3, 2), new(3, 3), new(4, 2), new(4, 3)
        }) board[cell.Reel][cell.Row] = "2";
        var payout = NeonDiscoCascade.EvaluateClusters(board, Game, Paytable, 100, 1);
        Assert.Equal(2, payout.Paylines.Count);
        Assert.All(payout.Paylines, line => Assert.Equal(4, Assert.Single(line.Matches).Match.MatchLength));
        Assert.Equal(274, payout.TotalPoints);
    }

    [Fact]
    public void ClearingClustersPreservesSurvivorOrderAndRefillsOnlyTheirGaps()
    {
        var board = NonWinningBoard();
        foreach (var cell in new GridPosition[] { new(0, 2), new(0, 3), new(1, 2), new(1, 3) })
            board[cell.Reel][cell.Row] = "2";
        var original = board.Select(reel => reel.ToArray()).ToArray();
        var draws = new Queue<int>([4, 5, 6, 4]);
        var result = Resolve(board, Reels(NeonDiscoDefinitions.SymbolIds), _ => draws.Dequeue());
        var step = Assert.Single(result.Steps);
        Assert.Empty(draws);
        Assert.Equal(4, step.Cleared.Count);
        for (var reel = 0; reel < 2; reel++)
        {
            Assert.Equal(original[reel][0], step.Reels[reel][2]);
            Assert.Equal(original[reel][1], step.Reels[reel][3]);
            Assert.Equal(new[] { 2, 2, 2, 2 }, step.DropRows[reel]);
        }
        for (var reel = 2; reel < 5; reel++)
        {
            Assert.Equal(original[reel], step.Reels[reel]);
            Assert.All(step.DropRows[reel], count => Assert.Equal(0, count));
        }
    }

    [Fact]
    public void CascadeLadderCarriesIntoTheNextPaidSpinAndDropsOneTierOnMisses()
    {
        var board = Enumerable.Range(0, 5).Select(_ => Enumerable.Repeat("2", 4).ToArray()).ToArray();
        NeonDiscoCascadeOutcome Spin(int startingMultiplier)
        {
            var finalDraws = NonWinningBoard().SelectMany(reel => reel)
                .Select(symbol => NeonDiscoDefinitions.SymbolIds.ToList().IndexOf(symbol));
            var draws = new Queue<int>(Enumerable.Repeat(0, 80).Concat(finalDraws));
            return Resolve(board, Reels(NeonDiscoDefinitions.SymbolIds), _ => draws.Dequeue(), startingMultiplier);
        }
        var result = Spin(1);
        Assert.Equal(5, result.Steps.Count);
        Assert.Equal(new[] { 1, 2, 3, 5, 5 }, result.Steps.Take(5).Select(step => step.Multiplier));
        Assert.All(result.Steps.Skip(3), step => Assert.Equal(5, step.Multiplier));
        Assert.Equal(result.Steps.Sum(step => step.Payout.TotalPoints), result.TotalPoints);
        Assert.Equal(246, result.Steps[0].Payout.TotalPoints);
        Assert.Equal(1_230, result.Steps[3].Payout.TotalPoints);
        var carried = NeonPlayPolicy.NextDiscoMultiplier(1, 11, result, null);
        Assert.Equal(5, carried);
        var nextSpin = Spin(carried);
        Assert.All(nextSpin.Steps, step => Assert.Equal(5, step.Multiplier));
        Assert.Equal(1_230, nextSpin.Steps[0].Payout.TotalPoints);

        var miss = Resolve(NonWinningBoard(), Reels(NeonDiscoDefinitions.SymbolIds), _ => throw new InvalidOperationException());
        Assert.Empty(miss.Steps);
        foreach (var tier in new[] { 3, 2, 1, 1 })
        {
            carried = NeonPlayPolicy.NextDiscoMultiplier(carried, 8, miss, null);
            Assert.Equal(tier, carried);
        }
        Assert.Equal(1, NeonPlayPolicy.NextDiscoMultiplier(5, 0, nextSpin, null));
        var trigger = NeonArcadeBonus.Apply(new(0, []), [["POWER"]]);
        Assert.Equal(1, NeonPlayPolicy.NextDiscoMultiplier(5, 12, null, trigger.Evidence));
    }

    [Fact]
    public void DiscoClusterAwardsExceedTheWagerAndPreviousDefinitionsRemainAvailable()
    {
        var source = new NeonDefinitionSource();
        var minimumWager = Game.Wagering.MinimumWagerPoints;
        Assert.Equal(50, minimumWager);
        Assert.Equal(137, Paytable.Rules.Min(rule => rule.Multiplier));
        Assert.Equal(143, Paytable.Rules.Single(rule => rule.SymbolId == "2" && rule.MatchLength == 5).Multiplier);
        Assert.Equal(69m, Math.Round(minimumWager * 137 * Game.Math.PayoutMultiplier, MidpointRounding.AwayFromZero));
        Assert.All(Paytable.Rules, rule => Assert.True(
            Math.Round(minimumWager * rule.Multiplier * Game.Math.PayoutMultiplier, MidpointRounding.AwayFromZero) >
            minimumWager));
        var previousPaytable = source.GetPaytable(NeonDiscoDefinitions.PreviousPaytableId)!;
        Assert.Equal(10, previousPaytable.Rules.Min(rule => rule.Multiplier));
        Assert.Equal(400, previousPaytable.Rules.Single(rule => rule.SymbolId == "2" && rule.MatchLength == 20).Multiplier);
        var previousPairedPaytable = source.GetPaytable(NeonDiscoDefinitions.PreviousPairedPaytableId)!;
        Assert.Equal(275, previousPairedPaytable.Rules.Min(rule => rule.Multiplier));
        var currentReels = source.GetReelSet(Game.Math.ReelSetId)!;
        var previousReels = source.GetReelSet(NeonDiscoDefinitions.PreviousReelSetId)!;
        Assert.False(currentReels.Reels[0].SequenceEqual(previousReels.Reels[0]));
        Assert.All(currentReels.Reels, strip =>
        {
            Assert.Equal(896, strip.Count);
            Assert.Equal(7, strip.Count(stop => stop == NeonDiscoDefinitions.WildSymbolId));
            Assert.All(NeonDiscoDefinitions.SymbolIds,
                symbol => Assert.Equal(127, strip.Count(stop => stop == symbol)));
        });
    }

    [Fact]
    public void ASettlementLimitCannotSilentlyDiscardAnUnpaidWinningBoard()
    {
        var board = Enumerable.Range(0, 5).Select(_ => Enumerable.Repeat("2", 4).ToArray()).ToArray();
        Assert.Throws<InvalidOperationException>(() => Resolve(board, Reels(["2"]), _ => 0));
    }

    [Fact]
    public void BaseSymbolsCannotEnterADiscoBoardOrRefillPool()
    {
        var board = NonWinningBoard();
        board[0][0] = "ACE";
        Assert.Throws<ArgumentException>(() => Resolve(board, Reels(NeonDiscoDefinitions.SymbolIds), _ => 0));
        Assert.Throws<ArgumentException>(() => Resolve(NonWinningBoard(), Reels(["FREE"]), _ => 0));
    }

    private static string[][] NonWinningBoard()
    {
        string[] fillers = ["4", "5", "7", "DISCO_MARTINI", "DISCO_SEVEN", "DISCO_BALL"];
        return Enumerable.Range(0, 5).Select(reel => Enumerable.Range(0, 4)
            .Select(row => fillers[(reel + row) % fillers.Length]).ToArray()).ToArray();
    }

    private static ReelSetDefinition Reels(IReadOnlyList<string> pool) => new()
    {
        Id = NeonDiscoDefinitions.ReelSetId, SymbolSetId = NeonDiscoDefinitions.SymbolSetId,
        Reels = Enumerable.Range(0, 5).Select(_ => pool.ToList()).ToList()
    };

    private static NeonDiscoCascadeOutcome Resolve(string[][] board, ReelSetDefinition reels, Func<int, int> draw,
        int startingMultiplier = 1) =>
        NeonDiscoCascade.Resolve(board, Game, reels, NeonDiscoDefinitions.CreateSymbolSet(),
            Paytable, 100, new(), new(), draw, out _, startingMultiplier);

    private sealed class DiscoFixtureDefinitions(string[][] board) : ISlotDefinitionSource
    {
        private readonly NeonDefinitionSource definitions = new();
        public GameDefinition? GetGame(string id) => definitions.GetGame(id);
        public SymbolSetDefinition? GetSymbolSet(string id) => definitions.GetSymbolSet(id);
        public PaytableDefinition? GetPaytable(string id) => definitions.GetPaytable(id);
        public ReelSetDefinition? GetReelSet(string id) => id == NeonDiscoDefinitions.ReelSetId
            ? ReelsFromBoard() : definitions.GetReelSet(id);
        private ReelSetDefinition ReelsFromBoard() => new()
        {
            Id = NeonDiscoDefinitions.ReelSetId, SymbolSetId = NeonDiscoDefinitions.SymbolSetId,
            Reels = board.Select(reel => reel.ToList()).ToList()
        };
    }

    private sealed class FixtureRandom : IGameRandomSource
    {
        private readonly Random random = new(73);
        private int calls;
        public int Next(int maximumExclusive) => calls++ < 5 ? 0 : random.Next(maximumExclusive);
    }
}
