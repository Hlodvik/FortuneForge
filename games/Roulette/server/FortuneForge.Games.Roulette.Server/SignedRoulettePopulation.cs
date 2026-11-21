using FortuneForge.Games.Abstractions.ManagedPlayers;
namespace FortuneForge.Server.Games.Roulette;

/// <summary>Maps the generic signed roster and shared population policy into Roulette's table boundary.</summary>
public sealed class SignedRoulettePopulation(IManagedPlayerRosterClient roster, IManagedTablePopulationDirector director) : IRoulettePopulation
{
    public Guid? SelectTable(IReadOnlyList<RouletteOpenTable> candidates) => candidates.OrderBy(table => table.CreatedAtUtc).ThenBy(table => table.Id.ToString("N"), StringComparer.Ordinal).FirstOrDefault()?.Id;
    public RoulettePopulationDecision Observe(RoulettePopulationObservation value)
    {
        var decision = director.Observe(new("roulette", value.TableId, value.RoundNumber, value.HumanPlayers, value.ManagedPlayers,
            value.MinimumOccupancy, value.MaximumOccupancy, value.TableOpen));
        return new(decision.KeepTableOpen, decision.ManagedArrivals, decision.BotOnly);
    }
    public async Task<IReadOnlyList<RouletteManagedProfile>> ReserveAsync(string assignmentId, int count, IReadOnlyCollection<string> excludedIds, DateTime nowUtc, CancellationToken token) =>
        (await roster.ReserveAsync(assignmentId, count, excludedIds, nowUtc, token)).Select(profile => new RouletteManagedProfile(profile.UserId, profile.PlayerName)).ToArray();
    public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) => roster.HeartbeatAsync(assignmentId, profileIds, nowUtc, token);
    public Task<bool> TryRenewAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) => roster.TryRenewAsync(assignmentId, profileIds, nowUtc, token);
    public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) => roster.ReleaseAsync(assignmentId, profileIds, nowUtc, token);
    public int DepartureRound(string tableId, string profileId, int joinedRound) => ManagedPlayerTableStayPolicy.DepartureRound("roulette", tableId, profileId, joinedRound);
    public bool ShouldLeave(int currentRound, int departureRound) => ManagedPlayerTableStayPolicy.ShouldLeave(currentRound, departureRound);
    public void Close(string tableId) => director.Close("roulette", tableId);
}
