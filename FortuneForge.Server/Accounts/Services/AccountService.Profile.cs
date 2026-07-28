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
    public async Task<AccountResult<AccountSummary>> GetProfileAsync(
        string? token,
        CancellationToken cancellationToken)
    {
        var storedAccount = await AuthenticateAsync(token, cancellationToken);
        return storedAccount is null
            ? AccountResult<AccountSummary>.Failure(AccountError.Unauthorized)
            : AccountResult<AccountSummary>.Success(storedAccount.Account);
    }

    public async Task<AccountAccessContext?> GetAccessContextAsync(
        string? token,
        CancellationToken cancellationToken)
    {
        var storedAccount = await AuthenticateAsync(token, cancellationToken);
        return storedAccount is null
            ? null
            : new AccountAccessContext(storedAccount.Account.UserId, storedAccount.Account.Role);
    }

    public async Task<AccountResult<AccountSummary>> UpdateProfileAsync(
        string? token,
        UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var storedAccount = await AuthenticateAsync(token, cancellationToken);
        if (storedAccount is null)
        {
            return AccountResult<AccountSummary>.Failure(AccountError.Unauthorized);
        }

        var playerName = NormalizePlayerName(request.PlayerName);
        if (playerName is null)
        {
            return AccountResult<AccountSummary>.Failure(AccountError.InvalidPlayerName);
        }

        var updateResult = await accountStore.UpdatePlayerNameAsync(
            storedAccount.Account.UserId,
            playerName,
            NormalizePlayerNameForLookup(playerName),
            cancellationToken);

        return updateResult.Value is null
            ? AccountResult<AccountSummary>.Failure(updateResult.Error)
            : AccountResult<AccountSummary>.Success(updateResult.Value.Account);
    }

    public async Task<AccountResult<AccountSummary>> ChangePasswordAsync(
        string? token,
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var storedAccount = await AuthenticateAsync(token, cancellationToken);
        if (storedAccount is null)
        {
            return AccountResult<AccountSummary>.Failure(AccountError.Unauthorized);
        }

        if (!passwordHashingService.Verify(request.CurrentPassword ?? string.Empty, storedAccount.PasswordHash))
        {
            return AccountResult<AccountSummary>.Failure(AccountError.InvalidCredentials);
        }

        if (!IsValidPassword(request.NewPassword))
        {
            return AccountResult<AccountSummary>.Failure(AccountError.InvalidPassword);
        }

        var wasUpdated = await accountStore.UpdatePasswordHashAsync(
            storedAccount.Account.UserId,
            passwordHashingService.Hash(request.NewPassword),
            DateTime.UtcNow,
            cancellationToken);
        return wasUpdated
            ? AccountResult<AccountSummary>.Success(storedAccount.Account)
            : AccountResult<AccountSummary>.Failure(AccountError.AccountNotFound);
    }

    public async Task<AccountResult<AccountSummary>> LogoutAsync(
        string? token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return AccountResult<AccountSummary>.Failure(AccountError.Unauthorized);
        }

        var storedAccount = await AuthenticateAsync(token, cancellationToken);
        if (storedAccount is null)
        {
            return AccountResult<AccountSummary>.Failure(AccountError.Unauthorized);
        }

        await accountStore.RevokeSessionAsync(
            HashToken(token),
            DateTime.UtcNow,
            cancellationToken);
        return AccountResult<AccountSummary>.Success(storedAccount.Account);
    }

    public async Task<AccountResult<AccountSummary>> DeactivateAsync(
        string? token,
        DeleteAccountRequest request,
        CancellationToken cancellationToken)
    {
        var storedAccount = await AuthenticateAsync(token, cancellationToken);
        if (storedAccount is null)
        {
            return AccountResult<AccountSummary>.Failure(AccountError.Unauthorized);
        }

        if (!passwordHashingService.Verify(request.Password ?? string.Empty, storedAccount.PasswordHash))
        {
            return AccountResult<AccountSummary>.Failure(AccountError.InvalidCredentials);
        }

        var wasDeactivated = await accountStore.DeactivateAsync(
            storedAccount.Account.UserId,
            cancellationToken);
        return wasDeactivated
            ? AccountResult<AccountSummary>.Success(storedAccount.Account)
            : AccountResult<AccountSummary>.Failure(AccountError.AccountNotFound);
    }
}
