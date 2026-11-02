using FortuneForge.Server.Payments.Models;

namespace FortuneForge.Server.Payments.Storage;

internal interface IWithdrawalHistoryReader
{
    Task<IReadOnlyList<StoredPaymentWithdrawal>> ListWithdrawalsAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken);
}
