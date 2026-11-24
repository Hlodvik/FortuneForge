namespace FortuneForge.Server.Accounts.Storage;

internal sealed record SlotPreparedOperationRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost)
{
    internal static bool IsValidId(string? operationId) => operationId is { Length: >= 16 and <= 128 } &&
        operationId.All(value => char.IsAsciiLetterOrDigit(value) || value is '-' or '_');
}

public sealed partial class FirestoreAccountStore
{
    // Generic legacy reconciliation rejects records carrying a prepared outcome.
    private const string PreparedOperationIdField = "preparedOperationId";
    private const string PreparedOutcomeSha256Field = "preparedOutcomeSha256";
    private const string PreparedRoundJsonField = "preparedRoundJson";
}
