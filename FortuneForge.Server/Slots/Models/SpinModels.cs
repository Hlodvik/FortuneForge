namespace FortuneForge.Server.Slots.Models;

public readonly record struct GridPosition(int Reel, int Row);

public sealed record SymbolMatch(
    int PaylineId,
    string SymbolId,
    int MatchLength,
    IReadOnlyList<GridPosition> Positions,
    IReadOnlyList<GridPosition> WildPositions);

public sealed record MatchCandidate(IReadOnlyList<SymbolMatch> Matches);

public sealed record PaylineEvaluation(
    int PaylineId,
    IReadOnlyList<MatchCandidate> Candidates);

public sealed record PaidMatch(SymbolMatch Match, long Multiplier, long AmountPoints);

public sealed record PaylinePayout(
    int PaylineId,
    long AmountPoints,
    IReadOnlyList<PaidMatch> Matches);

public sealed record SpinPayout(long TotalPoints, IReadOnlyList<PaylinePayout> Paylines);

public sealed record ReelOutcome(
    IReadOnlyList<int> StopIndexes,
    IReadOnlyList<IReadOnlyList<string>> VisibleReels);

public sealed record SpinResult(
    Guid SpinId,
    string GameId,
    string ReelSetId,
    string SymbolSetId,
    string PaytableId,
    long WagerPoints,
    decimal PointValueInCents,
    IReadOnlyList<int> ReelStops,
    IReadOnlyList<IReadOnlyList<string>> Reels,
    SpinPayout Payout);
