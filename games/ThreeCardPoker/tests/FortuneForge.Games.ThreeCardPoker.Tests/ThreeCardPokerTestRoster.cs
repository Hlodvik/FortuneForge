using FortuneForge.Games.Abstractions.ManagedPlayers;

namespace FortuneForge.Games.ThreeCardPoker.Tests;

internal sealed class ThreeCardPokerTestRoster : IManagedPlayerRosterClient
{
    public int Reservations { get; private set; }
    public int Heartbeats { get; private set; }
    public int Releases { get; private set; }
    public bool LoseNextReleaseReply { get; set; }
    public Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveAsync(string assignmentId, int count, IReadOnlyCollection<string> excludedProfileIds,
        DateTime nowUtc, CancellationToken token)
    { Reservations++; throw new InvalidOperationException("Private Three Card Poker tables do not reserve managed profiles."); }
    public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token)
    { Heartbeats++; return Task.CompletedTask; }
    public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token)
    {
        Releases++;
        if (LoseNextReleaseReply) { LoseNextReleaseReply = false; throw new ManagedPlayerRosterUnavailableException(); }
        return Task.CompletedTask;
    }
}
