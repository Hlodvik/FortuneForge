using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace FortuneForge.Games.Mines;

public enum MinesRoundStatus { Active, Lost, CashedOut, Cleared }
public enum MinesTileKind { Hidden, Gem, Mine }

/// <summary>Private server persistence model. NEVER return this type from a player API; use MinesRoundView.</summary>
public sealed record MinesRoundState
{
    [JsonConstructor]
    public MinesRoundState(long wagerCents, int mineCount, ImmutableArray<int> minePositions,
        ImmutableArray<int> revealedTiles, MinesRoundStatus status, long payoutCents)
    {
        WagerCents = wagerCents;
        MineCount = mineCount;
        MinePositions = minePositions;
        RevealedTiles = revealedTiles;
        Status = status;
        PayoutCents = payoutCents;
        Validate();
    }

    public long WagerCents { get; }
    public int MineCount { get; }
    public ImmutableArray<int> MinePositions { get; }
    public ImmutableArray<int> RevealedTiles { get; }
    public MinesRoundStatus Status { get; }
    public long PayoutCents { get; }
    [JsonIgnore] public int SafeReveals => RevealedTiles.Count(tile => !MinePositions.Contains(tile));

    public void Validate()
    {
        MinesRules.ValidateWager(WagerCents);
        MinesRules.ValidateMineCount(MineCount);
        if (MinePositions.IsDefault || MinePositions.Length != MineCount ||
            MinePositions.Any(tile => tile is < 0 or >= MinesRules.TileCount) ||
            MinePositions.Distinct().Count() != MineCount || !MinePositions.SequenceEqual(MinePositions.Order()))
            throw new ArgumentException("The persisted mine board is invalid.", nameof(MinePositions));
        if (RevealedTiles.IsDefault || RevealedTiles.Any(tile => tile is < 0 or >= MinesRules.TileCount) ||
            RevealedTiles.Distinct().Count() != RevealedTiles.Length)
            throw new ArgumentException("The persisted reveal sequence is invalid.", nameof(RevealedTiles));
        if (!Enum.IsDefined(Status)) throw new ArgumentOutOfRangeException(nameof(Status));
        var minesHit = RevealedTiles.Count(tile => MinePositions.Contains(tile));
        var safe = SafeReveals;
        var safeTotal = MinesRules.TileCount - MineCount;
        var valid = Status switch
        {
            MinesRoundStatus.Active => minesHit == 0 && safe < safeTotal && PayoutCents == 0,
            MinesRoundStatus.Lost => minesHit == 1 && safe < safeTotal &&
                MinePositions.Contains(RevealedTiles[^1]) && PayoutCents == 0,
            MinesRoundStatus.CashedOut => minesHit == 0 && safe > 0 && safe < safeTotal &&
                PayoutCents == MinesRules.ReturnCents(WagerCents, MineCount, safe),
            MinesRoundStatus.Cleared => minesHit == 0 && safe == safeTotal &&
                PayoutCents == MinesRules.ReturnCents(WagerCents, MineCount, safe),
            _ => false,
        };
        if (!valid) throw new ArgumentException("The persisted round outcome or payout is invalid.", nameof(Status));
    }
}

public sealed record MinesTileView(int Index, bool IsRevealed, MinesTileKind Kind);

/// <summary>Player-safe projection: no unrevealed board, server random data, or storage fields.</summary>
public sealed record MinesRoundView(
    long WagerCents,
    int MineCount,
    MinesRoundStatus Status,
    int SafeReveals,
    int GemsRemaining,
    decimal CurrentMultiplier,
    decimal? NextMultiplier,
    long CashOutCents,
    bool CanCashOut,
    long PayoutCents,
    ImmutableArray<MinesTileView> Tiles);
