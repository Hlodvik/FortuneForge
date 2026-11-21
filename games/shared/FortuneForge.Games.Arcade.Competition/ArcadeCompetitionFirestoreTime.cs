namespace FortuneForge.Server.Arcade.Competition;

internal static class ArcadeCompetitionFirestoreTime
{
    internal static DateTimeOffset FloorToMicrosecond(DateTimeOffset time) =>
        new(time.UtcTicks - time.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
}
