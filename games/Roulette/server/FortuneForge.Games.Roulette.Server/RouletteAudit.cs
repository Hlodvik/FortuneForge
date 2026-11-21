using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Roulette;
namespace FortuneForge.Server.Games.Roulette;
public static class RouletteAudit
{
    public const string Version = "roulette-outcome-v1";
    public static IReadOnlyList<RoundAuditFinding> Check(RouletteRoundState state)
    {
        var valid = state.Phase == RouletteRoundPhase.Settled && state.WinningPocket is { } pocket &&
            state.Bets.Length > 0 && state.Bets.Length == state.Settlements.Length &&
            state.Bets.Select((bet, index) => ValidRouletteSettlement(bet, state.Settlements[index], pocket.Number)).All(value => value);
        return Result("roulette-outcome-mismatch", valid,
            "The single-zero pocket and each virtual-credit bet return match independent rule arithmetic.");
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
    private static IReadOnlyList<RoundAuditFinding> Result(string code, bool valid, string success) => valid
        ? [new("audit-scope", "info", success + " Command history, RNG provenance and behavioral cheating remain outside this check.")]
        : [new(code, "error", "The recorded terminal state or settlement disagrees with an independent rule calculation.")];
}
