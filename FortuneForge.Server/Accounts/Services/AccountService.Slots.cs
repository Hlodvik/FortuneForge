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
            admission,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task<SlotSpinAdmission> BeginSlotSpinAsync(
        string userId,
        string gameId,
        long wagerPoints,
        decimal pointValueInCents,
        bool useFreeSpin,
        bool useSpecialBoost,
        int specialBoostCost,
        DateTime startedAtUtc,
        TimeSpan cooldown,
        CancellationToken cancellationToken,
        Func<SlotSpinAdmission, CancellationToken, Task<SpinResult>>? resolveOutcome = null)
    {
        return accountStore.BeginSlotSpinAsync(
            userId,
            gameId,
            wagerPoints,
            pointValueInCents,
            useFreeSpin,
            useSpecialBoost,
            specialBoostCost,
            startedAtUtc,
            cooldown,
            cancellationToken,
            resolveOutcome);
    }

    public Task<SlotStateResponse> GetSlotStateAsync(
        string userId,
        string gameId,
        decimal pointValueInCents,
        CancellationToken cancellationToken) =>
        accountStore.GetSlotStateAsync(userId, gameId, pointValueInCents, cancellationToken);

    public Task<NeonCarRespinSettlement> RespinNeonCarReelAsync(
        string userId, Guid spinId, int reelIndex, CancellationToken cancellationToken) =>
        accountStore.RespinNeonCarReelAsync(userId, spinId, reelIndex, cancellationToken);

    public Task DismissNeonCarRespinAsync(
        string userId, Guid spinId, CancellationToken cancellationToken) =>
        accountStore.DismissNeonCarRespinAsync(userId, spinId, cancellationToken);

    public Task<NeonNitroChoiceFinalization> FinalizeNeonNitroChoiceAsync(
        string userId, Guid spinId, int reelIndex, CancellationToken cancellationToken) =>
        accountStore.FinalizeNeonNitroChoiceAsync(userId, spinId, reelIndex, cancellationToken);

    public Task<NeonCarRespinSettlement> FinalizeNeonCarRespinNitroChoiceAsync(
        string userId, Guid spinId, int reelIndex, CancellationToken cancellationToken) =>
        accountStore.FinalizeNeonCarRespinNitroChoiceAsync(userId, spinId, reelIndex, cancellationToken);

    public Task<bool> GrantNeonAdminSpecialPlaysAsync(
        string userId,
        long wagerPoints,
        decimal pointValueInCents,
        bool arcadeRows,
        bool discoBall,
        CancellationToken cancellationToken) =>
        accountStore.GrantNeonAdminSpecialPlaysAsync(
            userId, wagerPoints, pointValueInCents, arcadeRows, discoBall, cancellationToken);

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
