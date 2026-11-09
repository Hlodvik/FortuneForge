using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

public sealed record NeonDiscoCascadeAudit(IReadOnlyList<int> DrawIndexes);
public sealed record NeonDiscoCascadeStep(IReadOnlyList<GridPosition> Cleared,
    IReadOnlyList<IReadOnlyList<string>> Reels,
    IReadOnlyList<IReadOnlyList<int>> DropRows, SpinPayout Payout);
public sealed record NeonDiscoCascadeOutcome(IReadOnlyList<IReadOnlyList<string>> InitialReels,
    IReadOnlyList<NeonDiscoCascadeStep> Steps, long TotalPoints);

public sealed record NeonEngineAudit(
    string EngineVersion, DateTime GeneratedAtUtc,
    GameDefinition BaseGame, GameDefinition EffectiveGame,
    ReelSetDefinition ReelSet, SymbolSetDefinition SymbolSet, PaytableDefinition Paytable,
    int PityMissesBefore, long EnergyBalanceBefore, string? FreeSpinFeatureMode,
    int GenerationAttempts, IReadOnlyList<IReadOnlyList<string>> RawGeneratedReels,
    IReadOnlyList<IReadOnlyList<string>> ReelsAfterFreeGameFrequency,
    NeonArcadeEvidence NeonArcade, NeonDiscoCascadeAudit? NeonDiscoCascade);

public sealed record NeonSpinOutcome(
    Guid SpinId, string GameId, string ReelSetId, string SymbolSetId, string PaytableId,
    long WagerPoints, decimal PointValueInCents, IReadOnlyList<int> ReelStops,
    IReadOnlyList<IReadOnlyList<string>> Reels, SpinPayout Payout,
    int ConsecutiveFiveMisses, bool FiveMatchPityTriggered, int FreeSpinsAwarded,
    int SpecialPointsAwarded, int EnergyAwarded, bool SpecialBoostApplied)
{
    public bool IsFreeSpin { get; init; }
    public long BananaBonusPoints { get; init; }
    public required NeonArcadeEvidence NeonArcade { get; init; }
    public NeonDiscoCascadeOutcome? NeonDiscoCascade { get; init; }
    public required NeonEngineAudit EngineAudit { get; init; }
}
