namespace FortuneForge.Server.Admin.Operations;

internal interface IAdminOperationsStore
{
    Task<AdminOperationsSnapshot> ReadAsync(
        AdminOperationsRange range,
        CancellationToken cancellationToken);

    Task AppendAuthorizedAccessAuditAsync(
        string actorUserId,
        string operation,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken);
}
