namespace FortuneForge.Games.Dice;

public readonly record struct DieValue
{
    public DieValue(int value)
    {
        if (value is < 1 or > 6)
            throw new ArgumentOutOfRangeException(nameof(value), "A standard die value must be from one through six.");

        Value = value;
    }

    public int Value { get; }

    public void Validate()
    {
        if (Value is < 1 or > 6)
            throw new InvalidOperationException("A standard die value must be from one through six.");
    }

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
