using SharedManagedPlayers = FortuneForge.Games.Abstractions.ManagedPlayers;

namespace FortuneForge.Server.Bots;

internal static class ManagedPlayerSkillLevels
{
    public const int Poor = SharedManagedPlayers.ManagedPlayerSkillLevels.Poor;
    public const int Average = SharedManagedPlayers.ManagedPlayerSkillLevels.Average;
    public const int Strong = SharedManagedPlayers.ManagedPlayerSkillLevels.Strong;

    public static void Validate(int value) => SharedManagedPlayers.ManagedPlayerSkillLevels.Validate(value);
}

internal sealed class ManagedPlayerDecisionOptions : SharedManagedPlayers.ManagedPlayerDecisionOptions;

internal sealed class DeterministicManagedPlayerRandom(ulong seed, string stream)
{
    private readonly SharedManagedPlayers.DeterministicManagedPlayerRandom random = new(seed, stream);

    public int Next(int exclusiveMaximum) => random.Next(exclusiveMaximum);

    public double NextDouble() => random.NextDouble();

    public T Choose<T>(IReadOnlyList<T> values) => random.Choose(values);
}
