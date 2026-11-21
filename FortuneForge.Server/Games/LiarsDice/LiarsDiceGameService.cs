using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.LiarsDice;
using FortuneForge.Server.Bots.LiarsDice;
using FortuneForge.Server.SecurityAudit;

namespace FortuneForge.Server.Games.LiarsDice;

/// <summary>Compatibility adapter for memory-only match IDs issued before durable workers.</summary>
public sealed class LiarsDiceGameService : LegacyLiarsDiceGameService
{
    public LiarsDiceGameService(FortuneForge.Server.Bots.LiarsDice.ILiarsDiceManagedPlayerRoster roster,
        TimeProvider timeProvider, IGameAnalyticsSink? analytics = null, IGameSecurityEventSink? securityEvents = null)
        : base(new RosterAdapter(roster), timeProvider, analytics, async (observation, completion, token) =>
        {
            if (securityEvents is null) return;
            try
            {
                if (completion is null) await securityEvents.WriteAsync(observation, token);
                else await securityEvents.WriteCompletedAsync(observation, completion, token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { throw new GameSecurityRecordingException(exception); }
        }) { }
    public new async Task<FortuneForge.Games.LiarsDice.LiarsDiceMatchResponse> StartAsync(string userId,
        FortuneForge.Games.LiarsDice.StartLiarsDiceMatchRequest request, CancellationToken token = default)
    {
        try { return await base.StartAsync(userId, request, token); }
        catch (LiarsDiceManagedPlayerSupplyException) { throw new FortuneForge.Server.Bots.ManagedPlayerSupplyUnavailableException(); }
    }
    private sealed class RosterAdapter(FortuneForge.Server.Bots.LiarsDice.ILiarsDiceManagedPlayerRoster roster)
        : FortuneForge.Games.LiarsDice.ILiarsDiceManagedPlayerRoster
    {
        public async Task<IReadOnlyList<FortuneForge.Games.LiarsDice.LiarsDiceManagedPlayer>> ReserveAsync(string assignmentId,
            int count, IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken token) =>
            await roster.ReserveAsync(assignmentId, count, excludedProfileIds, nowUtc, token);
        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) =>
            roster.HeartbeatAsync(assignmentId, profileIds, nowUtc, token);
        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) =>
            roster.ReleaseAsync(assignmentId, profileIds, nowUtc, token);
    }
}
