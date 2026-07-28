namespace FortuneForge.Server.Payments;

internal static class WithdrawalStatusProjection
{
    private static readonly HashSet<string> NegativeTerminalStatuses =
    [
        "cancelled",
        "failed",
        "rejected",
        "reversed"
    ];

    public static string? FromMerchantGatewayEvent(string eventType) =>
        eventType.Trim().ToLowerInvariant() switch
        {
            "withdrawal.created" => "pending",
            "withdrawal.pending" => "pending",
            "withdrawal.processing" => "processing",
            "withdrawal.completed" => "completed",
            "withdrawal.rejected" => "rejected",
            "withdrawal.failed" => "failed",
            "withdrawal.cancelled" => "cancelled",
            "withdrawal.canceled" => "cancelled",
            "withdrawal.reversed" => "reversed",
            _ => null
        };

    public static string? NormalizeProviderStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "created" => "pending",
            "pending" => "pending",
            "processing" => "processing",
            "completed" => "completed",
            "rejected" => "rejected",
            "failed" => "failed",
            "cancelled" => "cancelled",
            "canceled" => "cancelled",
            "reversed" => "reversed",
            _ => null
        };

    public static bool CanApply(string currentStatus, string nextStatus)
    {
        var current = NormalizeStoredStatus(currentStatus);
        var next = NormalizeProviderStatus(nextStatus);
        if (current is null || next is null)
        {
            return false;
        }

        if (string.Equals(current, next, StringComparison.Ordinal))
        {
            return true;
        }

        if (IsTerminal(current))
        {
            return false;
        }

        return current switch
        {
            "received" => next is "pending" or "processing" or "completed" ||
                IsNegativeTerminal(next),
            "pending" => next is "processing" or "completed" ||
                IsNegativeTerminal(next),
            "processing" => next is "completed" || IsNegativeTerminal(next),
            _ => false
        };
    }

    public static bool IsNegativeTerminal(string status) =>
        NegativeTerminalStatuses.Contains(status.Trim().ToLowerInvariant());

    public static bool IsTerminal(string status)
    {
        var normalized = NormalizeStoredStatus(status);
        return normalized is not null &&
            (string.Equals(normalized, "completed", StringComparison.Ordinal) ||
                IsNegativeTerminal(normalized));
    }

    public static string NoticeFor(string status) =>
        status switch
        {
            "pending" => "Withdrawal request is pending payment provider processing.",
            "processing" => "Withdrawal request is being processed. Reserved credits remain held.",
            "completed" => "Withdrawal payout completed. Reserved credits were finalized.",
            "rejected" => "Withdrawal request was rejected. Reserved credits were returned.",
            "failed" => "Withdrawal request failed. Reserved credits were returned.",
            "cancelled" => "Withdrawal request was cancelled. Reserved credits were returned.",
            "reversed" => "Withdrawal request was reversed. Reserved credits were returned.",
            _ => "Withdrawal status was updated by the payment provider."
        };

    private static string? NormalizeStoredStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "received" => "received",
            "created" => "pending",
            "pending" => "pending",
            "processing" => "processing",
            "completed" => "completed",
            "rejected" => "rejected",
            "failed" => "failed",
            "cancelled" => "cancelled",
            "canceled" => "cancelled",
            "reversed" => "reversed",
            _ => null
        };
}
