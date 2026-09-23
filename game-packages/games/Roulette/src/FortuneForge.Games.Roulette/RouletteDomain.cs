using System.Collections.Immutable;

namespace FortuneForge.Games.Roulette;

public enum RouletteBetKind
{
    Straight,
    Split,
    Street,
    Corner,
    SixLine,
    Column,
    Dozen,
    Red,
    Black,
    Even,
    Odd,
    Low,
    High,
}

public enum RouletteRoundPhase
{
    Open,
    Settled,
}

public readonly record struct RoulettePocket
{
    public RoulettePocket(int number)
    {
        if (number is < 0 or > 36)
            throw new ArgumentOutOfRangeException(nameof(number), "A single-zero Roulette pocket must be from 0 through 36.");
        Number = number;
    }

    public int Number { get; }

    public bool IsZero => Number == 0;

    public bool IsRed => Number is 1 or 3 or 5 or 7 or 9 or 12 or 14 or 16 or 18
        or 19 or 21 or 23 or 25 or 27 or 30 or 32 or 34 or 36;
}

public sealed record RouletteBet(
    string PlayerId,
    RouletteBetKind Kind,
    decimal Stake,
    int? Number = null,
    ImmutableArray<int> Numbers = default);

public sealed record RouletteSettlement(
    string PlayerId,
    RouletteBetKind Kind,
    decimal Stake,
    bool Won,
    decimal TotalReturn);

public sealed record RouletteRoundState(
    string RoundId,
    RouletteRoundPhase Phase,
    ImmutableArray<RouletteBet> Bets,
    RoulettePocket? WinningPocket,
    ImmutableArray<RouletteSettlement> Settlements);

public sealed record RouletteSpinOutcome(
    RouletteRoundState State,
    RoulettePocket WinningPocket,
    ImmutableArray<RouletteSettlement> Settlements);

public sealed class RouletteRuleException(string message) : InvalidOperationException(message);
