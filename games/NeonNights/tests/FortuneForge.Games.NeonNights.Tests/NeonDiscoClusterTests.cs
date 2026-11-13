using FortuneForge.Games.NeonNights;
using FortuneForge.Games.Slots;
using Xunit;

namespace FortuneForge.Games.NeonNights.Tests;

public sealed class NeonDiscoClusterTests
{
    private static readonly GameDefinition Game = NeonDiscoDefinitions.CreateGame(NeonArcadeSlotDefinitions.CreateGame());
    private static readonly PaytableDefinition Paytable = NeonDiscoDefinitions.CreatePaytable();

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
        Assert.All(currentReels.Reels, strip => Assert.All(NeonDiscoDefinitions.SymbolIds,
            symbol => Assert.Equal(128, strip.Count(stop => stop == symbol))));
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
}
