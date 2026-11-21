namespace FortuneForge.Server.Games.Roulette;

public sealed record RouletteOpenTable(Guid Id, DateTime CreatedAtUtc);
public sealed record RouletteManagedProfile(string UserId, string PlayerName);
public sealed record RoulettePopulationObservation(string TableId, int RoundNumber, int HumanPlayers,
    int ManagedPlayers, int MinimumOccupancy, int MaximumOccupancy, bool TableOpen);
public sealed record RoulettePopulationDecision(bool KeepTableOpen, int ManagedArrivals, bool BotOnly);

/// <summary>The host adapts its generic player directory; all Roulette table decisions remain package-owned.</summary>
public interface IRoulettePopulation
{
    Guid? SelectTable(IReadOnlyList<RouletteOpenTable> candidates);
    RoulettePopulationDecision Observe(RoulettePopulationObservation observation);
    Task<IReadOnlyList<RouletteManagedProfile>> ReserveAsync(string assignmentId, int count,
        IReadOnlyCollection<string> excludedIds, DateTime nowUtc, CancellationToken token);
    Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) => Task.CompletedTask;
    async Task<bool> TryRenewAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token)
    {
        await HeartbeatAsync(assignmentId, profileIds, nowUtc, token);
        return true;
    }
    Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token);
    int DepartureRound(string tableId, string profileId, int joinedRound);
    bool ShouldLeave(int currentRound, int departureRound);
    void Close(string tableId);
}
public sealed class NoRoulettePopulation : IRoulettePopulation
{
    public Guid? SelectTable(IReadOnlyList<RouletteOpenTable> candidates) => candidates.OrderBy(table => table.CreatedAtUtc).ThenBy(table => table.Id).FirstOrDefault()?.Id;
    public RoulettePopulationDecision Observe(RoulettePopulationObservation value) => new(value.TableOpen && value.HumanPlayers + value.ManagedPlayers > 0, 0, value.HumanPlayers == 0);
    public Task<IReadOnlyList<RouletteManagedProfile>> ReserveAsync(string assignmentId, int count, IReadOnlyCollection<string> excludedIds, DateTime nowUtc, CancellationToken token) => Task.FromResult<IReadOnlyList<RouletteManagedProfile>>([]);
    public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) => Task.CompletedTask;
    public int DepartureRound(string tableId, string profileId, int joinedRound) => int.MaxValue;
    public bool ShouldLeave(int currentRound, int departureRound) => currentRound >= departureRound;
    public void Close(string tableId) { }
}
public interface IRouletteEventRecorder
{
    Task WriteAsync(RouletteGameService.RouletteAuditSnapshot snapshot, CancellationToken token);
}
public sealed class RouletteConflictException(string message) : InvalidOperationException(message);
