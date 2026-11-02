using System.Collections.Immutable;
using System.Security.Cryptography;

namespace FortuneForge.Games.Mines;

/// <summary>Server-only dependency; deterministic substitutions belong in tests, never player requests.</summary>
public interface IMinesBoardGenerator
{
    ImmutableArray<int> Generate(int mineCount);
}

/// <summary>Uniform partial Fisher-Yates selection without replacement using unbiased secure RNG.</summary>
public sealed class SecureMinesBoardGenerator : IMinesBoardGenerator
{
    public ImmutableArray<int> Generate(int mineCount)
    {
        MinesRules.ValidateMineCount(mineCount);
        var tiles = Enumerable.Range(0, MinesRules.TileCount).ToArray();
        for (var i = 0; i < mineCount; i++)
        {
            var next = RandomNumberGenerator.GetInt32(i, MinesRules.TileCount);
            (tiles[i], tiles[next]) = (tiles[next], tiles[i]);
        }
        return [.. tiles.Take(mineCount).Order()];
    }
}

/// <summary>Pure transitions over a board fixed at start; host must transact wallet/state/idempotency atomically.</summary>
public sealed class MinesEngine(IMinesBoardGenerator? boardGenerator = null)
{
    private readonly IMinesBoardGenerator _boards = boardGenerator ?? new SecureMinesBoardGenerator();

    public MinesRoundState Start(long wagerCents, int mineCount)
    {
        // Validate before asking RNG to generate the board. The host accepts/debits this wager atomically.
        MinesRules.ValidateWager(wagerCents);
        MinesRules.ValidateMineCount(mineCount);
        return new(wagerCents, mineCount, _boards.Generate(mineCount), [], MinesRoundStatus.Active, 0,
            MinesRules.HouseEdgeBasisPoints, MinesRules.PayoutPolicyVersion);
    }

    public MinesRoundState Reveal(MinesRoundState round, int tileIndex)
    {
        EnsureActive(round);
        if (tileIndex is < 0 or >= MinesRules.TileCount) throw new ArgumentOutOfRangeException(nameof(tileIndex));
        if (round.RevealedTiles.Contains(tileIndex)) throw new InvalidOperationException("This tile is already revealed.");
        var revealed = round.RevealedTiles.Add(tileIndex);
        var hitMine = round.MinePositions.Contains(tileIndex);
        var payout = hitMine ? MinesRules.MineReturnCents(round.WagerCents, round.MineCount, round.SafeReveals,
            round.HouseEdgeBasisPoints, round.PayoutPolicyVersion)
            : revealed.Length == MinesRules.TileCount - round.MineCount
                ? MinesRules.ReturnCents(round.WagerCents, round.MineCount, revealed.Length, round.HouseEdgeBasisPoints, round.PayoutPolicyVersion) : 0;
        var status = hitMine ? payout > 0 ? MinesRoundStatus.Consolation : MinesRoundStatus.Lost
            : revealed.Length == MinesRules.TileCount - round.MineCount ? MinesRoundStatus.Cleared
            : MinesRoundStatus.Active;
        return new(round.WagerCents, round.MineCount, round.MinePositions, revealed, status, payout,
            round.HouseEdgeBasisPoints, round.PayoutPolicyVersion);
    }

    public MinesRoundState CashOut(MinesRoundState round)
    {
        EnsureActive(round);
        if (round.SafeReveals == 0) throw new InvalidOperationException("Reveal a gem before cashing out.");
        return new(round.WagerCents, round.MineCount, round.MinePositions, round.RevealedTiles,
            MinesRoundStatus.CashedOut, MinesRules.ReturnCents(round.WagerCents, round.MineCount, round.SafeReveals, round.HouseEdgeBasisPoints, round.PayoutPolicyVersion),
            round.HouseEdgeBasisPoints, round.PayoutPolicyVersion);
    }

    public MinesRoundView Project(MinesRoundState round)
    {
        ArgumentNullException.ThrowIfNull(round);
        round.Validate();
        var active = round.Status == MinesRoundStatus.Active;
        var safe = round.SafeReveals;
        return new(round.WagerCents, round.MineCount, round.Status, safe,
            MinesRules.TileCount - round.MineCount - safe,
            round.Status == MinesRoundStatus.Lost ? 0m : round.Status == MinesRoundStatus.Consolation
                ? MinesRules.MineMultiplier(round.MineCount, safe, round.HouseEdgeBasisPoints, round.PayoutPolicyVersion)
                : MinesRules.Multiplier(round.MineCount, safe, round.HouseEdgeBasisPoints, round.PayoutPolicyVersion),
            active ? MinesRules.Multiplier(round.MineCount, safe + 1, round.HouseEdgeBasisPoints, round.PayoutPolicyVersion) : null,
            active ? MinesRules.ReturnCents(round.WagerCents, round.MineCount, safe, round.HouseEdgeBasisPoints, round.PayoutPolicyVersion) : 0,
            active && safe > 0, round.PayoutCents,
            [.. Enumerable.Range(0, MinesRules.TileCount).Select(index =>
                new MinesTileView(index, round.RevealedTiles.Contains(index),
                    active && !round.RevealedTiles.Contains(index) ? MinesTileKind.Hidden
                    : round.MinePositions.Contains(index) ? MinesTileKind.Mine : MinesTileKind.Gem))],
            round.HouseEdgeBasisPoints, round.PayoutPolicyVersion,
            active ? MinesRules.MineReturnCents(round.WagerCents, round.MineCount, safe, round.HouseEdgeBasisPoints, round.PayoutPolicyVersion) : 0);
    }

    private static void EnsureActive(MinesRoundState round)
    {
        ArgumentNullException.ThrowIfNull(round);
        round.Validate();
        if (round.Status != MinesRoundStatus.Active) throw new InvalidOperationException("This round has ended.");
    }
}
