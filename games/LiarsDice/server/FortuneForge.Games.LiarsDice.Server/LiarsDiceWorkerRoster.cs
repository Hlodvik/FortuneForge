using FortuneForge.Games.Abstractions.ManagedPlayers;

namespace FortuneForge.Games.LiarsDice.Server;

public sealed class LiarsDiceWorkerRoster(IManagedPlayerRosterClient roster) : ILiarsDiceManagedPlayerRoster
{
    public async Task<IReadOnlyList<LiarsDiceManagedPlayer>> ReserveAsync(string assignmentId, int count,
        IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken token) =>
        (await roster.ReserveAsync(assignmentId, count, excludedProfileIds, nowUtc, token))
            .Select(player => new LiarsDiceManagedPlayer(player.UserId, player.PlayerName, player.SkillLevel)).ToArray();
    public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) =>
        roster.HeartbeatAsync(assignmentId, profileIds, nowUtc, token);
    public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) =>
        roster.ReleaseAsync(assignmentId, profileIds, nowUtc, token);
}
