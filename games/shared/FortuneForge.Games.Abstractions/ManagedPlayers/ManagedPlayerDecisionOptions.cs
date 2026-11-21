namespace FortuneForge.Games.Abstractions.ManagedPlayers;

public class ManagedPlayerDecisionOptions
{
    public double AverageErrorRate { get; init; } = 0.12;
    public double StrongImperfectionRate { get; init; } = 0.03;
}
