using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Firestore;
using FortuneForge.Games.TrickTaking;
using FortuneForge.Server.Games.Hearts;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Bots.Hearts;

public sealed record HeartsManagedPlayer(string UserId, string PlayerName, int SkillLevel);
public interface IHeartsManagedPlayerRoster
{
    Task<IReadOnlyList<HeartsManagedPlayer>> ReserveAsync(string assignmentId, int count,
        IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken cancellationToken);
    Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken);
    Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken);
}

internal sealed class HeartsManagedPlayerRoster(Func<CancellationToken, Task<bool>> allowsNewAssignments) : IHeartsManagedPlayerRoster
{
    internal HeartsManagedPlayerRoster(FirestoreDb database) : this(async token =>
        FirestoreManagedPlayerPolicy.AllowsBots(await database.Document(FirestoreManagedPlayerPolicy.StatePath).GetSnapshotAsync(token), HeartsBackend.GameId)) { }
    public async Task<IReadOnlyList<HeartsManagedPlayer>> ReserveAsync(string assignmentId, int count,
        IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignmentId);
        if (count is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(count));
        cancellationToken.ThrowIfCancellationRequested();
        if (count == 0) return [];
        if (!await allowsNewAssignments(cancellationToken)) return [];
        return new[] { PlayerSeat.East, PlayerSeat.South, PlayerSeat.West }.Take(count)
            .Select(seat => HeartsHousePlayers.Create(assignmentId, seat)).ToArray();
    }
    public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class HeartsManagedTurnWorker(FirestoreHeartsFreeStore games, ILogger<HeartsManagedTurnWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await games.AdvanceDueAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "A durable Hearts table could not advance its scheduled turn."); }
        }
    }
}
