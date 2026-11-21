namespace FortuneForge.Games.Abstractions.ManagedPlayers;

public static class ManagedPlayerSkillLevels
{
    public const int Poor = 2;
    public const int Average = 3;
    public const int Strong = 4;

    public static void Validate(int value)
    {
        if (value is not Poor and not Average and not Strong)
            throw new ArgumentOutOfRangeException(nameof(value), "Difficulty must be exactly 2, 3, or 4.");
    }
}
