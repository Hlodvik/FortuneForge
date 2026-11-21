using Shared = FortuneForge.Games.Abstractions.Matchmaking.ManagedQueueArrivalPolicy;

namespace FortuneForge.Server.Matchmaking;

internal static class ManagedQueueArrivalPolicy
{
    public static TimeSpan NextGap(string gameId, string cohortId, int arrivalOrdinal) =>
        Shared.NextGap(gameId, cohortId, arrivalOrdinal);

    public static TimeSpan FinalPause(string gameId, string cohortId) =>
        Shared.FinalPause(gameId, cohortId);
}
