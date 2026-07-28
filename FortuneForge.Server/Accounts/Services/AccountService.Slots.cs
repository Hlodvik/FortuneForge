using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Accounts;

public sealed partial class AccountService
{
    public Task<SlotSpinSettlement> RecordSlotSpinAsync(
        string userId,
        SpinResult result,
        SlotSpinAdmission admission,
        CancellationToken cancellationToken)
    {
        return accountStore.RecordSlotSpinAsync(
            userId,
            result,
            admission.ChargedWagerPoints,
            admission.IsFreeSpin,
            admission.FreeSpinFeatureMode,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<SlotSpinAdmission> BeginSlotSpinAsync(
        string userId,
        string gameId,
        long wagerPoints,
        bool useFreeSpin,
        bool useSpecialBoost,
        int specialBoostCost,
        DateTime startedAtUtc,
        TimeSpan cooldown,
        CancellationToken cancellationToken)
    {
        return accountStore.BeginSlotSpinAsync(
            userId,
            gameId,
            wagerPoints,
            useFreeSpin,
            useSpecialBoost,
            specialBoostCost,
            startedAtUtc,
            cooldown,
            cancellationToken);
    }

    public Task<SlotStateResponse> GetSlotStateAsync(
        string userId,
        string gameId,
        CancellationToken cancellationToken) =>
        accountStore.GetSlotStateAsync(userId, gameId, cancellationToken);

    public async Task<AccountResult<SlotHistoryResponse>> GetSlotHistoryAsync(
        string? token,
        int limit,
        CancellationToken cancellationToken)
    {
        var storedAccount = await AuthenticateAsync(token, cancellationToken);
        if (storedAccount is null)
        {
            return AccountResult<SlotHistoryResponse>.Failure(AccountError.Unauthorized);
        }

        var spins = await accountStore.GetSlotSpinHistoryAsync(
            storedAccount.Account.UserId,
            limit,
            cancellationToken);
        return AccountResult<SlotHistoryResponse>.Success(new SlotHistoryResponse(spins));
    }
}
