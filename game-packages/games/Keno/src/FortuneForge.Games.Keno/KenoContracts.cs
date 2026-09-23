using System.Collections.Immutable;

namespace FortuneForge.Games.Keno;

public sealed record KenoTicket
{
    public const int MinimumNumbers = 1;
    public const int MaximumNumbers = 10;
    public const int MaximumNumber = 80;

    public KenoTicket(IEnumerable<int> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);

        Numbers = [.. numbers.Order()];
        if (Numbers.Length is < MinimumNumbers or > MaximumNumbers)
            throw new ArgumentException("A Keno ticket must select from one through ten numbers.", nameof(numbers));
        if (Numbers.Distinct().Count() != Numbers.Length)
            throw new ArgumentException("A Keno ticket cannot contain duplicate numbers.", nameof(numbers));
        ValidateNumbers(Numbers, nameof(numbers));
    }

    public ImmutableArray<int> Numbers { get; }

    internal static void ValidateNumbers(IEnumerable<int> numbers, string parameterName)
    {
        if (numbers.Any(number => number is < 1 or > MaximumNumber))
            throw new ArgumentOutOfRangeException(parameterName, "A Keno number must be from one through eighty.");
    }
}

public sealed record KenoDraw
{
    public const int DrawCount = 20;

    public KenoDraw(IEnumerable<int> numbers)
    {
        ArgumentNullException.ThrowIfNull(numbers);

        Numbers = [.. numbers];
        if (Numbers.Length != DrawCount)
            throw new ArgumentException("A Keno draw must contain exactly twenty numbers.", nameof(numbers));
        if (Numbers.Distinct().Count() != DrawCount)
            throw new ArgumentException("A Keno draw cannot contain duplicate numbers.", nameof(numbers));
        KenoTicket.ValidateNumbers(Numbers, nameof(numbers));
    }

    public ImmutableArray<int> Numbers { get; }
}

public sealed record KenoPaytableOutcome(string PaytableId, int Value);

/// <summary>Provides a game-specific outcome without prescribing payout economics.</summary>
public interface IKenoPaytable
{
    KenoPaytableOutcome Evaluate(KenoTicket ticket, int hitCount);
}

public sealed record KenoRoundResult(
    KenoTicket Ticket,
    KenoDraw Draw,
    int HitCount,
    KenoPaytableOutcome? PaytableOutcome);

public sealed record KenoRound(KenoRoundResult Result);
