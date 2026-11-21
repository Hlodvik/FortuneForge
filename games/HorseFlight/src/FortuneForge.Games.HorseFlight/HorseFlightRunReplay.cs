namespace FortuneForge.Games.HorseFlight;

/// <summary>The recorded-run compatibility policy shared by paid and free Horse Flight runs.</summary>
public static class HorseFlightRunReplay
{
    public static int ResolveRulesVersion(long? storedVersion) => storedVersion switch
    {
        null => HorseFlightEngine.LegacyRulesVersion,
        HorseFlightEngine.LegacyRulesVersion or HorseFlightEngine.DraftMultiBiomeRulesVersion or
            HorseFlightEngine.PreviousRulesVersion or HorseFlightEngine.CurrentRulesVersion => checked((int)storedVersion.Value),
        _ => throw new InvalidOperationException("The Horse Flight rules version is corrupt."),
    };

    public static string PhaseName(HorseFlightPhase phase) => phase switch
    {
        HorseFlightPhase.Running => "running",
        HorseFlightPhase.ObstacleCollision => "obstacle-collision",
        HorseFlightPhase.Fell => "fell",
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };
}
