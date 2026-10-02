using System.Collections.Immutable;

namespace FortuneForge.Games.Roulette;

public static class RouletteEngine
{
    public static RouletteRoundState OpenRound(string roundId)
    {
        if (string.IsNullOrWhiteSpace(roundId))
            throw new ArgumentException("A round ID is required.", nameof(roundId));

        return new RouletteRoundState(
            roundId,
            RouletteRoundPhase.Open,
            [],
            null,
            []);
    }

    public static RouletteRoundState PlaceBet(RouletteRoundState state, RouletteBet bet)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(bet);
        if (state.Phase != RouletteRoundPhase.Open)
            throw new RouletteRuleException("Bets cannot be placed after the wheel has spun.");

        ValidateBet(bet);
        return state with { Bets = state.Bets.Add(bet) };
    }

    public static RouletteRoundState RemoveBet(RouletteRoundState state, int betIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Phase != RouletteRoundPhase.Open)
            throw new RouletteRuleException("Bets cannot be removed after the wheel has spun.");
        if ((uint)betIndex >= (uint)state.Bets.Length)
            throw new ArgumentOutOfRangeException(nameof(betIndex), "The Roulette bet does not exist.");

        return state with { Bets = state.Bets.RemoveAt(betIndex) };
    }

    public static RouletteRoundState ClearBets(RouletteRoundState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Phase != RouletteRoundPhase.Open)
            throw new RouletteRuleException("Bets cannot be cleared after the wheel has spun.");

        return state with { Bets = [] };
    }

    public static RouletteSpinOutcome Spin(RouletteRoundState state, RoulettePocket winningPocket)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Phase != RouletteRoundPhase.Open)
            throw new RouletteRuleException("The round has already been settled.");
        if (state.Bets.IsDefaultOrEmpty)
            throw new RouletteRuleException("At least one bet is required before spinning.");

        var settlements = state.Bets
            .Select(bet => Settle(bet, winningPocket))
            .ToImmutableArray();
        var settled = state with
        {
            Phase = RouletteRoundPhase.Settled,
            WinningPocket = winningPocket,
            Settlements = settlements,
        };
        return new RouletteSpinOutcome(settled, winningPocket, settlements);
    }

    public static RouletteSettlement Settle(RouletteBet bet, RoulettePocket winningPocket)
    {
        ArgumentNullException.ThrowIfNull(bet);
        ValidateBet(bet);
        var won = BetWins(bet, winningPocket);
        var multiplier = PayoutMultiplier(bet.Kind);
        return new RouletteSettlement(
            bet.PlayerId,
            bet.Kind,
            bet.Stake,
            won,
            won ? bet.Stake * multiplier : 0m);
    }

    private static bool BetWins(RouletteBet bet, RoulettePocket pocket) => bet.Kind switch
    {
        RouletteBetKind.Straight => bet.Number == pocket.Number,
        RouletteBetKind.Split or RouletteBetKind.Street or RouletteBetKind.Corner or RouletteBetKind.SixLine
            => bet.Numbers.Contains(pocket.Number),
        RouletteBetKind.Column => pocket.Number != 0 && pocket.Number % 3 == ColumnValue(bet) % 3,
        RouletteBetKind.Dozen => pocket.Number >= DozenValue(bet) * 12 - 11 && pocket.Number <= DozenValue(bet) * 12,
        RouletteBetKind.Red => !pocket.IsZero && pocket.IsRed,
        RouletteBetKind.Black => !pocket.IsZero && !pocket.IsRed,
        RouletteBetKind.Even => !pocket.IsZero && pocket.Number % 2 == 0,
        RouletteBetKind.Odd => !pocket.IsZero && pocket.Number % 2 != 0,
        RouletteBetKind.Low => pocket.Number is >= 1 and <= 18,
        RouletteBetKind.High => pocket.Number is >= 19 and <= 36,
        _ => throw new ArgumentOutOfRangeException(nameof(bet), "Unknown Roulette bet kind."),
    };

    private static void ValidateBet(RouletteBet bet)
    {
        if (string.IsNullOrWhiteSpace(bet.PlayerId))
            throw new ArgumentException("A player ID is required.", nameof(bet));
        if (bet.Stake <= 0m)
            throw new ArgumentOutOfRangeException(nameof(bet), "A Roulette stake must be positive.");
        if (!Enum.IsDefined(bet.Kind))
            throw new ArgumentOutOfRangeException(nameof(bet), "The Roulette bet kind is invalid.");
        if (bet.Kind == RouletteBetKind.Straight && bet.Number is not (>= 0 and <= 36))
            throw new ArgumentException("A straight bet requires a number from 0 through 36.", nameof(bet));
        if (bet.Kind == RouletteBetKind.Straight && !bet.Numbers.IsDefaultOrEmpty)
            throw new ArgumentException("A straight bet cannot specify a number collection.", nameof(bet));
        if (bet.Kind is not (RouletteBetKind.Straight or RouletteBetKind.Column or RouletteBetKind.Dozen) && bet.Number is not null)
            throw new ArgumentException("Only straight, column, and dozen bets may specify a number.", nameof(bet));

        var numbers = bet.Kind == RouletteBetKind.Straight
            ? ImmutableArray.Create(bet.Number!.Value)
            : bet.Numbers;
        var expectedCount = bet.Kind switch
        {
            RouletteBetKind.Split => 2,
            RouletteBetKind.Street => 3,
            RouletteBetKind.Corner => 4,
            RouletteBetKind.SixLine => 6,
            RouletteBetKind.Column or RouletteBetKind.Dozen => 0,
            RouletteBetKind.Red or RouletteBetKind.Black or RouletteBetKind.Even or RouletteBetKind.Odd or RouletteBetKind.Low or RouletteBetKind.High => 0,
            _ => 1,
        };
        if (expectedCount == 0 && !bet.Numbers.IsDefaultOrEmpty)
            throw new ArgumentException("This Roulette bet cannot specify covered numbers.", nameof(bet));
        if (expectedCount > 0 && numbers.Length != expectedCount)
            throw new ArgumentException($"{bet.Kind} bets must cover exactly {expectedCount} number(s).", nameof(bet));
        if (expectedCount > 0 && (numbers.Any(number => number is < 0 or > 36) || numbers.Distinct().Count() != expectedCount))
            throw new ArgumentException("A Roulette bet contains invalid or duplicate pockets.", nameof(bet));
        if (bet.Kind is RouletteBetKind.Split or RouletteBetKind.Street or RouletteBetKind.Corner or RouletteBetKind.SixLine)
            ValidateInsideShape(bet.Kind, numbers);
        if (bet.Kind == RouletteBetKind.Column && bet.Number is not (1 or 2 or 3))
            throw new ArgumentException("A column bet requires column 1, 2, or 3.", nameof(bet));
        if (bet.Kind == RouletteBetKind.Dozen && bet.Number is not (1 or 2 or 3))
            throw new ArgumentException("A dozen bet requires dozen 1, 2, or 3.", nameof(bet));
    }

    private static decimal PayoutMultiplier(RouletteBetKind kind) => kind switch
    {
        RouletteBetKind.Straight => 36m,
        RouletteBetKind.Split => 18m,
        RouletteBetKind.Street => 12m,
        RouletteBetKind.Corner => 9m,
        RouletteBetKind.SixLine => 6m,
        RouletteBetKind.Column or RouletteBetKind.Dozen => 3m,
        _ => 2m,
    };

    private static int ColumnValue(RouletteBet bet) => bet.Number!.Value;
    private static int DozenValue(RouletteBet bet) => bet.Number!.Value;

    private static void ValidateInsideShape(RouletteBetKind kind, ImmutableArray<int> numbers)
    {
        var ordered = numbers.Order().ToArray();
        var rows = ordered.Select(number => (number - 1) / 3).Distinct().ToArray();
        var columns = ordered.Select(number => (number - 1) % 3).Distinct().ToArray();
        var valid = kind switch
        {
            RouletteBetKind.Split => ordered.Contains(0)
                ? ordered.Length == 2 && ordered[0] == 0 && ordered[1] is >= 1 and <= 3
                : rows.Length == 1 && columns.Length == 2 && ordered[1] - ordered[0] == 1
                    || columns.Length == 1 && rows.Length == 2 && ordered[1] - ordered[0] == 3,
            RouletteBetKind.Street => ordered.Contains(0)
                ? ordered is [0, 1, 2] or [0, 2, 3]
                : rows.Length == 1 && columns.Length == 3,
            RouletteBetKind.Corner => rows.Length == 2 && columns.Length == 2 && ordered[1] - ordered[0] == 1 && ordered[2] - ordered[0] == 3 && ordered[3] - ordered[2] == 1,
            RouletteBetKind.SixLine => rows.Length == 2 && columns.Length == 3 && ordered[3] - ordered[0] == 3 && ordered[5] - ordered[2] == 3,
            _ => false,
        };
        if (!valid)
            throw new ArgumentException($"The {kind} numbers are not a valid Roulette layout.", nameof(numbers));
    }
}
