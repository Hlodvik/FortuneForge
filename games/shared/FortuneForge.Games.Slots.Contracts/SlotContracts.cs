namespace FortuneForge.Games.Slots;

// Data and infrastructure ports only. Game matching and payout policies belong
// to the game package, rather than to these shared transport contracts.
public interface ISlotDefinitionSource
{
    GameDefinition? GetGame(string id);
    SymbolSetDefinition? GetSymbolSet(string id);
    ReelSetDefinition? GetReelSet(string id);
    PaytableDefinition? GetPaytable(string id);
}

public interface IGameRandomSource
{
    int Next(int maximumExclusive);
}

public readonly record struct GridPosition(int Reel, int Row);
public sealed record SymbolMatch(int PaylineId, string SymbolId, int MatchLength,
    IReadOnlyList<GridPosition> Positions, IReadOnlyList<GridPosition> WildPositions);
public sealed record MatchCandidate(IReadOnlyList<SymbolMatch> Matches);
public sealed record PaylineEvaluation(int PaylineId, IReadOnlyList<MatchCandidate> Candidates);
public sealed record PaidMatch(SymbolMatch Match, long Multiplier, long AmountPoints);
public sealed record PaylinePayout(int PaylineId, long AmountPoints, IReadOnlyList<PaidMatch> Matches);
public sealed record SpinPayout(long TotalPoints, IReadOnlyList<PaylinePayout> Paylines);
public sealed record ReelOutcome(IReadOnlyList<int> StopIndexes,
    IReadOnlyList<IReadOnlyList<string>> VisibleReels);
