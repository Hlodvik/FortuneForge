using System.Collections.Immutable;
using FortuneForge.Games.Dice;

namespace FortuneForge.Games.SicBo;

public sealed record SicBoRoll
{
    public SicBoRoll(int first, int second, int third)
        : this([first, second, third])
    {
    }

    public SicBoRoll(IEnumerable<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        Values = [.. values];
        if (Values.Length != 3)
            throw new ArgumentException("A Sic Bo roll must contain exactly three dice values.", nameof(values));

        foreach (var value in Values)
            _ = new DieValue(value);
    }

    public ImmutableArray<int> Values { get; }

    public int Total => Values.Sum();

    public bool IsTriple => Values[0] == Values[1] && Values[1] == Values[2];

    public int OccurrencesOf(int face)
    {
        ValidateFace(face, nameof(face));
        return Values.Count(value => value == face);
    }

    internal static void ValidateFace(int face, string parameterName)
    {
        if (face is < 1 or > 6)
            throw new ArgumentOutOfRangeException(parameterName, "A Sic Bo face must be from one through six.");
    }
}

public enum SicBoBetKind
{
    Small,
    Big,
    Odd,
    Even,
    SingleNumber,
    Total,
    TwoNumberCombination,
    SpecificDouble,
    AnyTriple,
    SpecificTriple,
}

public readonly record struct SicBoTwoNumberCombination
{
    private SicBoTwoNumberCombination(int firstFace, int secondFace)
    {
        FirstFace = firstFace;
        SecondFace = secondFace;
    }

    public int FirstFace { get; }

    public int SecondFace { get; }

    public static SicBoTwoNumberCombination Create(int firstFace, int secondFace)
    {
        SicBoRoll.ValidateFace(firstFace, nameof(firstFace));
        SicBoRoll.ValidateFace(secondFace, nameof(secondFace));
        if (firstFace == secondFace)
            throw new ArgumentException("A Sic Bo two-number combination requires distinct faces.", nameof(secondFace));

        return firstFace < secondFace
            ? new SicBoTwoNumberCombination(firstFace, secondFace)
            : new SicBoTwoNumberCombination(secondFace, firstFace);
    }
}

public sealed record SicBoBet
{
    private SicBoBet(SicBoBetKind kind, decimal stake, int? face, int? total, SicBoTwoNumberCombination? combination)
    {
        Kind = kind;
        Stake = stake;
        Face = face;
        Total = total;
        Combination = combination;
    }

    public SicBoBetKind Kind { get; }

    public decimal Stake { get; }

    public int? Face { get; }

    public int? Total { get; }

    public SicBoTwoNumberCombination? Combination { get; }

    public static SicBoBet Small(decimal stake) => CreateWithoutSelection(SicBoBetKind.Small, stake);
    public static SicBoBet Big(decimal stake) => CreateWithoutSelection(SicBoBetKind.Big, stake);
    public static SicBoBet Odd(decimal stake) => CreateWithoutSelection(SicBoBetKind.Odd, stake);
    public static SicBoBet Even(decimal stake) => CreateWithoutSelection(SicBoBetKind.Even, stake);
    public static SicBoBet AnyTriple(decimal stake) => CreateWithoutSelection(SicBoBetKind.AnyTriple, stake);

    public static SicBoBet SingleNumber(decimal stake, int face) => CreateWithFace(SicBoBetKind.SingleNumber, stake, face);
    public static SicBoBet SpecificDouble(decimal stake, int face) => CreateWithFace(SicBoBetKind.SpecificDouble, stake, face);
    public static SicBoBet SpecificTriple(decimal stake, int face) => CreateWithFace(SicBoBetKind.SpecificTriple, stake, face);

    public static SicBoBet ForTotal(decimal stake, int total)
    {
        ValidateStake(stake);
        if (total is < 4 or > 17)
            throw new ArgumentOutOfRangeException(nameof(total), "A Sic Bo total bet must select from four through seventeen.");
        return new SicBoBet(SicBoBetKind.Total, stake, null, total, null);
    }

    public static SicBoBet TwoNumberCombination(decimal stake, int firstFace, int secondFace)
    {
        ValidateStake(stake);
        return new SicBoBet(SicBoBetKind.TwoNumberCombination, stake, null, null, SicBoTwoNumberCombination.Create(firstFace, secondFace));
    }

    public static SicBoBet CreateWithoutSelection(SicBoBetKind kind, decimal stake)
    {
        ValidateStake(stake);
        if (kind is not SicBoBetKind.Small and not SicBoBetKind.Big and not SicBoBetKind.Odd and not SicBoBetKind.Even and not SicBoBetKind.AnyTriple)
            throw new ArgumentOutOfRangeException(nameof(kind), "This Sic Bo bet kind requires a selection or is invalid.");
        return new SicBoBet(kind, stake, null, null, null);
    }

    private static SicBoBet CreateWithFace(SicBoBetKind kind, decimal stake, int face)
    {
        ValidateStake(stake);
        SicBoRoll.ValidateFace(face, nameof(face));
        return new SicBoBet(kind, stake, face, null, null);
    }

    private static void ValidateStake(decimal stake)
    {
        if (stake <= 0m)
            throw new ArgumentOutOfRangeException(nameof(stake), "A Sic Bo stake must be positive.");
    }
}
