namespace FortuneForge.Server.Bots;

// Optional durable support for all-or-nothing supply. A same-table caller may
// share a provisional lease, but only a completed batch makes it usable.
internal interface IManagedPlayerReservationBatchStore
{
    bool ProvisionalBatchesEnabled { get; }
    Task<bool> TryReserveBatchAsync(string profileId, string gameId, string assignmentId,
        string batchId, DateTime nowUtc, CancellationToken cancellationToken);
    Task<bool> CompleteBatchAsync(string gameId, string assignmentId, string batchId,
        IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken cancellationToken);
    Task<bool> ReleaseIncompleteBatchAsync(string profileId, string assignmentId,
        string batchId, DateTime nowUtc, CancellationToken cancellationToken);
}
