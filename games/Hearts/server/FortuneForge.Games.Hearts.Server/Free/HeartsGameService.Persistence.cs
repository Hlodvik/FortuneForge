using FortuneForge.Server.Bots.Hearts;

namespace FortuneForge.Server.Games.Hearts;

internal sealed record HeartsManagedActorSnapshot(HeartsManagedPlayer Profile, ulong DecisionSeed, ulong TimingSeed,
    int DecisionVersion, int TimingVersion);
internal sealed record HeartsFreeSessionSnapshot(Guid Id, string UserId, HeartsMatchState State, int RequestedSkillLevel,
    Dictionary<PlayerSeat, HeartsManagedActorSnapshot> ManagedActors, DateTime StartedAtUtc, bool IsTrial,
    DateTime? CompletedAtUtc, bool AnalyticsRecorded, bool IsCancelled, bool IsExpired, bool TrialCompletionPending,
    bool ManagedReleasePending, long EventVersion, bool AuditExpiredRecorded, DateTime LastActivityAtUtc,
    DateTime LeaseSafeUntilUtc, bool ManagedPlayersReleased, PlayerSeat? ScheduledManagedSeat,
    DateTime? NextManagedActionAtUtc, CompletedTrick? LastCompletedTrick, string Message, DateTime NextLeaseHeartbeatAtUtc);

public sealed partial class HeartsGameService
{
    internal void Restore(IEnumerable<HeartsFreeSessionSnapshot> snapshots)
    {
        foreach (var value in snapshots)
        {
            var session = new HeartsSession(value.Id, value.UserId, value.State, value.RequestedSkillLevel,
                value.ManagedActors.ToDictionary(pair => pair.Key, pair => new ManagedActor(pair.Value)), value.StartedAtUtc, value.IsTrial)
            {
                CompletedAtUtc = value.CompletedAtUtc, AnalyticsRecorded = value.AnalyticsRecorded,
                IsCancelled = value.IsCancelled, IsExpired = value.IsExpired, TrialCompletionPending = value.TrialCompletionPending,
                ManagedReleasePending = value.ManagedReleasePending, EventVersion = value.EventVersion,
                AuditExpiredRecorded = value.AuditExpiredRecorded, LastActivityAtUtc = value.LastActivityAtUtc,
                LeaseSafeUntilUtc = value.LeaseSafeUntilUtc, ManagedPlayersReleased = value.ManagedPlayersReleased,
                LastCompletedTrick = value.LastCompletedTrick, Message = value.Message,
            };
            session.RestoreHeartbeat(value.NextLeaseHeartbeatAtUtc);
            if (value.ScheduledManagedSeat is { } seat && value.NextManagedActionAtUtc is { } scheduled)
                session.ScheduleTurn(seat, scheduled);
            if (!sessions.TryAdd(value.Id, session)) throw new InvalidOperationException("Duplicate private Hearts session.");
        }
    }

    internal HeartsFreeSessionSnapshot[] Snapshot() => sessions.Values.Select(session =>
    {
        lock (session.SyncRoot)
            return new HeartsFreeSessionSnapshot(session.Id, session.UserId, session.State, session.RequestedSkillLevel,
                session.ManagedActors.ToDictionary(pair => pair.Key, pair => pair.Value.Snapshot()), session.StartedAtUtc,
                session.IsTrial, session.CompletedAtUtc, session.AnalyticsRecorded, session.IsCancelled, session.IsExpired,
                session.TrialCompletionPending, session.ManagedReleasePending, session.EventVersion, session.AuditExpiredRecorded,
                session.LastActivityAtUtc, session.LeaseSafeUntilUtc, session.ManagedPlayersReleased, session.ScheduledManagedSeat,
                session.NextManagedActionAtUtc, session.LastCompletedTrick, session.Message, session.NextHeartbeatAtUtc);
    }).OrderBy(session => session.Id).ToArray();
}

public sealed class HeartsAdmissionPausedException() : InvalidOperationException("New Hearts tables are paused.");
