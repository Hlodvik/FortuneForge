using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Craps;
using FortuneForge.Games.DropMerge;
using FortuneForge.Games.Roulette;
using FortuneForge.Games.TwentyFortyEight;

namespace FortuneForge.Server.SecurityAudit;

internal sealed record PracticeCrapsExtraBet(string Kind, decimal Stake, bool Resolved, bool Won, decimal? TotalReturn);

/// <summary>Independent terminal/outcome arithmetic for non-credit games; not an RNG or full command replay attestation.</summary>
internal static class PracticeOutcomeAudit
{
    internal const string TwentyFortyEightVersion = "2048-terminal-outcome-v1";
    internal const string DropMergeVersion = "drop-merge-terminal-outcome-v1";
    internal const string CrapsVersion = "craps-outcome-v1";
    internal const string RouletteVersion = "roulette-outcome-v1";

    internal static IReadOnlyList<RoundAuditFinding> Check(TwentyFortyEightState state)
    {
        var tiles = state.Tiles;
        var valid = state.Size == 4 && tiles.Length == 16 && state.Moves > 0 && state.Score >= 0 &&
            tiles.All(PowerOfTwoOrZero) && state.HighestTile == tiles.Max() &&
            (state.Phase == TwentyFortyEightPhase.Won && state.HighestTile >= 2048 && state.Score >= 2048 ||
             state.Phase == TwentyFortyEightPhase.Lost && !CanSlide(tiles, state.Size));
        return Result("2048-terminal-mismatch", valid,
            "The terminal 2048 board, phase, score and highest tile are internally consistent.");
    }

    internal static IReadOnlyList<RoundAuditFinding> Check(DropMergeState state, int dropColumn, int dropRow)
    {
        var tiles = state.Tiles;
        var blocked = dropColumn is >= 0 and < 7 && tiles.Length == 49 && tiles[dropColumn] != 0;
        var valid = state.Columns == 7 && state.Rows == 7 && tiles.Length == 49 &&
            state.Moves >= 0 && state.Score >= 0 && state.TotalMerges >= 0 &&
            tiles.All(PowerOfTwoOrZero) && state.HighestTile == tiles.Max() &&
            PowerOfTwo(state.CurrentTile) && PowerOfTwo(state.NextTile) &&
            state.Phase == DropMergePhase.Lost &&
            (dropRow == -1 && blocked || dropRow >= 0 && tiles.All(tile => tile != 0));
        return Result("drop-merge-terminal-mismatch", valid,
            "The terminal Drop Merge board and loss condition are internally consistent.");
    }

    internal static IReadOnlyList<RoundAuditFinding> Check(CrapsPassLineState state,
        IReadOnlyList<PracticeCrapsExtraBet> extras)
    {
        var rolls = state.Rolls;
        if (state.Phase != CrapsPassLinePhase.Resolved || rolls.IsDefaultOrEmpty || state.Bet.Stake <= 0 ||
            rolls.Any(dice => dice.First.Value is < 1 or > 6 || dice.Second.Value is < 1 or > 6))
            return Result("craps-outcome-mismatch", false, "The ordered dice, pass-line result or side-bet return disagrees with the rules.");
        var first = rolls[0].Total;
        var last = rolls[^1];
        CrapsRollResult expected;
        decimal returned;
        if (first is 7 or 11)
        {
            expected = CrapsRollResult.NaturalWin;
            returned = state.Bet.Stake * 2;
        }
        else if (first is 2 or 3 or 12)
        {
            expected = CrapsRollResult.CrapsLoss;
            returned = 0;
        }
        else if (rolls.Length >= 2 && first is 4 or 5 or 6 or 8 or 9 or 10 &&
                 rolls.Skip(1).Take(rolls.Length - 2).All(dice => dice.Total != first && dice.Total != 7) &&
                 (last.Total == 7 || last.Total == first))
        {
            expected = last.Total == first ? CrapsRollResult.PointHit : CrapsRollResult.SevenOut;
            returned = expected == CrapsRollResult.PointHit ? state.Bet.Stake * 2 : 0;
        }
        else return Result("craps-outcome-mismatch", false, "The ordered dice, pass-line result or side-bet return disagrees with the rules.");
        var point = expected is CrapsRollResult.PointHit or CrapsRollResult.SevenOut ? first : (int?)null;
        var valid = state.Point == point && state.LastOutcome is { IsTerminal: true } outcome &&
            outcome.Dice == last && outcome.Result == expected && outcome.TotalReturn == returned &&
            (point is not null || rolls.Length == 1) && extras.All(extra => ValidExtra(extra, first, point, expected));
        return Result("craps-outcome-mismatch", valid,
            "Ordered dice, pass-line return and every recorded virtual side bet match independent rule arithmetic.");
    }

    internal static IReadOnlyList<RoundAuditFinding> Check(RouletteRoundState state)
    {
        var valid = state.Phase == RouletteRoundPhase.Settled && state.WinningPocket is { } pocket &&
            state.Bets.Length > 0 && state.Bets.Length == state.Settlements.Length &&
            state.Bets.Select((bet, index) => ValidRouletteSettlement(bet, state.Settlements[index], pocket.Number)).All(value => value);
        return Result("roulette-outcome-mismatch", valid,
            "The single-zero pocket and each virtual-credit bet return match independent rule arithmetic.");
    }

    private static bool ValidExtra(PracticeCrapsExtraBet bet, int first, int? point, CrapsRollResult result)
    {
        if (bet.Stake <= 0 || !bet.Resolved) return false;
        var expected = bet.Kind switch
        {
            "field" when first is 2 or 12 => bet.Stake * 3,
            "field" when first is 3 or 4 or 9 or 10 or 11 => bet.Stake * 2,
            "any-seven" when first == 7 => bet.Stake * 5,
            "any-craps" when first is 2 or 3 or 12 => bet.Stake * 8,
            "odds" when point is 4 or 10 && result == CrapsRollResult.PointHit => bet.Stake * 3,
            "odds" when point is 5 or 9 && result == CrapsRollResult.PointHit => bet.Stake * 2.5m,
            "odds" when point is 6 or 8 && result == CrapsRollResult.PointHit => bet.Stake * 2.2m,
            "field" or "any-seven" or "any-craps" or "odds" => 0m,
            _ => -1m
        };
        return expected >= 0 && bet.TotalReturn == expected && bet.Won == (expected > 0);
    }

    private static bool ValidRouletteSettlement(RouletteBet bet, RouletteSettlement settlement, int pocket)
    {
        if (string.IsNullOrWhiteSpace(bet.PlayerId) || bet.Stake <= 0 || !Enum.IsDefined(bet.Kind) || pocket is < 0 or > 36 ||
            bet.PlayerId != settlement.PlayerId || bet.Kind != settlement.Kind || bet.Stake != settlement.Stake)
            return false;
        var numbers = bet.Numbers.IsDefault ? [] : bet.Numbers.ToArray();
        if (numbers.Distinct().Count() != numbers.Length || numbers.Any(number => number is < 0 or > 36)) return false;
        var won = bet.Kind switch
        {
            RouletteBetKind.Straight when bet.Number is >= 0 and <= 36 && numbers.Length == 0 => bet.Number == pocket,
            RouletteBetKind.Split when numbers.Length == 2 => numbers.Contains(pocket),
            RouletteBetKind.Street when numbers.Length == 3 => numbers.Contains(pocket),
            RouletteBetKind.Corner when numbers.Length == 4 => numbers.Contains(pocket),
            RouletteBetKind.SixLine when numbers.Length == 6 => numbers.Contains(pocket),
            RouletteBetKind.Column when bet.Number is >= 1 and <= 3 && numbers.Length == 0 => pocket > 0 && pocket % 3 == bet.Number % 3,
            RouletteBetKind.Dozen when bet.Number is >= 1 and <= 3 && numbers.Length == 0 => pocket >= (bet.Number * 12 - 11) && pocket <= bet.Number * 12,
            RouletteBetKind.Red when bet.Number is null && numbers.Length == 0 => Red(pocket),
            RouletteBetKind.Black when bet.Number is null && numbers.Length == 0 => pocket > 0 && !Red(pocket),
            RouletteBetKind.Even when bet.Number is null && numbers.Length == 0 => pocket > 0 && pocket % 2 == 0,
            RouletteBetKind.Odd when bet.Number is null && numbers.Length == 0 => pocket > 0 && pocket % 2 != 0,
            RouletteBetKind.Low when bet.Number is null && numbers.Length == 0 => pocket is >= 1 and <= 18,
            RouletteBetKind.High when bet.Number is null && numbers.Length == 0 => pocket is >= 19 and <= 36,
            _ => false
        };
        var validShape = bet.Kind switch
        {
            RouletteBetKind.Split or RouletteBetKind.Street or RouletteBetKind.Corner or RouletteBetKind.SixLine =>
                bet.Number is null && InsideShape(bet.Kind, numbers),
            RouletteBetKind.Straight => bet.Number is >= 0 and <= 36 && numbers.Length == 0,
            RouletteBetKind.Column or RouletteBetKind.Dozen => bet.Number is >= 1 and <= 3 && numbers.Length == 0,
            _ => bet.Number is null && numbers.Length == 0
        };
        var multiplier = bet.Kind switch
        {
            RouletteBetKind.Straight => 36m, RouletteBetKind.Split => 18m, RouletteBetKind.Street => 12m,
            RouletteBetKind.Corner => 9m, RouletteBetKind.SixLine => 6m,
            RouletteBetKind.Column or RouletteBetKind.Dozen => 3m, _ => 2m
        };
        return validShape && settlement.Won == won && settlement.TotalReturn == (won ? bet.Stake * multiplier : 0);
    }

    private static bool InsideShape(RouletteBetKind kind, int[] numbers)
    {
        var sorted = numbers.Order().ToArray();
        if (sorted.Length == 0) return false;
        var rows = sorted.Where(value => value > 0).Select(value => (value - 1) / 3).Distinct().ToArray();
        var columns = sorted.Where(value => value > 0).Select(value => (value - 1) % 3).Distinct().ToArray();
        return kind switch
        {
            RouletteBetKind.Split => sorted.Length == 2 && (sorted[0] == 0 && sorted[1] is >= 1 and <= 3 ||
                sorted[0] > 0 && (rows.Length == 1 && sorted[1] - sorted[0] == 1 || columns.Length == 1 && sorted[1] - sorted[0] == 3)),
            RouletteBetKind.Street => sorted.Length == 3 &&
                (sorted.SequenceEqual([0, 1, 2]) || sorted.SequenceEqual([0, 2, 3]) || sorted[0] > 0 && rows.Length == 1 && columns.Length == 3),
            RouletteBetKind.Corner => sorted.Length == 4 && sorted[0] > 0 && rows.Length == 2 && columns.Length == 2 &&
                sorted[1] - sorted[0] == 1 && sorted[2] - sorted[0] == 3 && sorted[3] - sorted[2] == 1,
            RouletteBetKind.SixLine => sorted.Length == 6 && sorted[0] > 0 && rows.Length == 2 && columns.Length == 3 &&
                sorted[3] - sorted[0] == 3 && sorted[5] - sorted[2] == 3,
            _ => false
        };
    }

    private static bool Red(int pocket) => pocket is 1 or 3 or 5 or 7 or 9 or 12 or 14 or 16 or 18 or 19
        or 21 or 23 or 25 or 27 or 30 or 32 or 34 or 36;
    private static bool PowerOfTwoOrZero(int tile) => tile == 0 || PowerOfTwo(tile);
    private static bool PowerOfTwo(int tile) => tile >= 2 && (tile & (tile - 1)) == 0;

    private static bool CanSlide(IReadOnlyList<int> tiles, int size)
    {
        for (var index = 0; index < tiles.Count; index++)
        {
            if (tiles[index] == 0) return true;
            if (index % size + 1 < size && tiles[index] == tiles[index + 1]) return true;
            if (index / size + 1 < size && tiles[index] == tiles[index + size]) return true;
        }
        return false;
    }

    private static IReadOnlyList<RoundAuditFinding> Result(string code, bool valid, string success) => valid
        ? [new("audit-scope", "info", success + " Command history, RNG provenance and behavioral cheating remain outside this check.")]
        : [new(code, "error", "The recorded terminal state or settlement disagrees with an independent rule calculation.")];
}
