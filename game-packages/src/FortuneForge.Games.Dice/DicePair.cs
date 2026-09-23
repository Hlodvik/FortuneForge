namespace FortuneForge.Games.Dice;

public readonly record struct DicePair(DieValue First, DieValue Second)
{
    public int Total => First.Value + Second.Value;

    public bool IsPair => First == Second;

    public void Validate()
    {
        First.Validate();
        Second.Validate();
    }
}
