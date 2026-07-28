using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Accounts;

public sealed partial class AccountService(
    IAccountStore accountStore,
    IPasswordHashingService passwordHashingService,
    FirebaseEmailVerificationService firebaseEmailVerificationService,
    IHttpContextAccessor httpContextAccessor)
{
    private const string ActiveStatus = "active";
    private const string PendingEmailVerificationStatus = "pending-email-verification";
    private const int MinimumPasswordLength = 8;
    private const int MaximumPasswordLength = 128;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

    public async Task<AccountResult<CreateAccountResponse>> CreateAsync(
        CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var playerName = NormalizePlayerName(request.PlayerName);
        if (playerName is null)
        {
            return AccountResult<CreateAccountResponse>.Failure(AccountError.InvalidPlayerName);
        }

        var email = NormalizeEmail(request.Email);
        if (email is null)
        {
            return AccountResult<CreateAccountResponse>.Failure(AccountError.InvalidEmail);
        }

        if (!IsValidPassword(request.Password))
        {
            return AccountResult<CreateAccountResponse>.Failure(AccountError.InvalidPassword);
        }

        if (await accountStore.FindByEmailAsync(email, cancellationToken) is not null)
        {
            return AccountResult<CreateAccountResponse>.Failure(AccountError.EmailTaken);
        }

        var firebaseRegistration = await firebaseEmailVerificationService.CreateUserAsync(
            email,
            request.Password,
            cancellationToken);
        if (firebaseRegistration.Value is null)
        {
            return AccountResult<CreateAccountResponse>.Failure(firebaseRegistration.Error);
        }

        var creationResult = await accountStore.CreateAsync(
            firebaseRegistration.Value.UserId,
            playerName,
            NormalizePlayerNameForLookup(playerName),
            email,
            passwordHashingService.Hash(request.Password),
            PendingEmailVerificationStatus,
            cancellationToken);
        if (creationResult.Value is null)
        {
            await firebaseEmailVerificationService.DeleteUserIfPresentAsync(
                firebaseRegistration.Value.UserId,
                cancellationToken);
            return AccountResult<CreateAccountResponse>.Failure(creationResult.Error);
        }

        var verificationEmailSent =
            await firebaseEmailVerificationService.SendVerificationEmailAsync(
                firebaseRegistration.Value.IdToken,
                cancellationToken);
        return AccountResult<CreateAccountResponse>.Success(new CreateAccountResponse(
            creationResult.Value.Account,
            true,
            verificationEmailSent));
    }

    public async Task<AccountResult<AuthenticationResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null || string.IsNullOrEmpty(request.Password))
        {
            return AccountResult<AuthenticationResponse>.Failure(AccountError.InvalidCredentials);
        }

        var storedAccount = await accountStore.FindByEmailAsync(email, cancellationToken);
        if (storedAccount is null ||
            !passwordHashingService.Verify(request.Password, storedAccount.PasswordHash))
        {
            return AccountResult<AuthenticationResponse>.Failure(AccountError.InvalidCredentials);
        }

        if (storedAccount.Deactivated)
        {
            return AccountResult<AuthenticationResponse>.Failure(AccountError.Deactivated);
        }

        if (storedAccount.Status == PendingEmailVerificationStatus)
        {
            var isVerified = await firebaseEmailVerificationService.IsEmailVerifiedAsync(
                email,
                cancellationToken);
            if (!isVerified)
            {
                return AccountResult<AuthenticationResponse>.Failure(AccountError.EmailNotVerified);
            }

            var activationResult = await accountStore.ActivateEmailVerifiedAsync(
                storedAccount.Account.UserId,
                DateTime.UtcNow,
                cancellationToken);
            if (activationResult.Value is null)
            {
                return AccountResult<AuthenticationResponse>.Failure(activationResult.Error);
            }

            storedAccount = activationResult.Value;
        }

        if (storedAccount.Status != ActiveStatus)
        {
            return AccountResult<AuthenticationResponse>.Failure(AccountError.InvalidCredentials);
        }

        return AccountResult<AuthenticationResponse>.Success(
            await IssueSessionAsync(storedAccount.Account, cancellationToken));
    }

    public async Task<AccountResult<ResendVerificationResponse>> ResendVerificationAsync(
        ResendVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null || string.IsNullOrEmpty(request.Password))
        {
            return AccountResult<ResendVerificationResponse>.Failure(AccountError.InvalidCredentials);
        }

        var storedAccount = await accountStore.FindByEmailAsync(email, cancellationToken);
        if (storedAccount is null ||
            !passwordHashingService.Verify(request.Password, storedAccount.PasswordHash))
        {
            return AccountResult<ResendVerificationResponse>.Failure(AccountError.InvalidCredentials);
        }

        if (storedAccount.Deactivated)
        {
            return AccountResult<ResendVerificationResponse>.Failure(AccountError.Deactivated);
        }

        if (storedAccount.Status == ActiveStatus)
        {
            return AccountResult<ResendVerificationResponse>.Success(
                new ResendVerificationResponse(true, false));
        }

        var isVerified = await firebaseEmailVerificationService.IsEmailVerifiedAsync(
            email,
            cancellationToken);
        if (isVerified)
        {
            var activation = await accountStore.ActivateEmailVerifiedAsync(
                storedAccount.Account.UserId,
                DateTime.UtcNow,
                cancellationToken);
            return activation.Value is null
                ? AccountResult<ResendVerificationResponse>.Failure(activation.Error)
                : AccountResult<ResendVerificationResponse>.Success(
                    new ResendVerificationResponse(true, false));
        }

        var resendError = await firebaseEmailVerificationService.ResendVerificationEmailAsync(
            email,
            request.Password,
            cancellationToken);
        return resendError == AccountError.None
            ? AccountResult<ResendVerificationResponse>.Success(
                new ResendVerificationResponse(false, true))
            : AccountResult<ResendVerificationResponse>.Failure(resendError);
    }

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

    private async Task<AuthenticationResponse> IssueSessionAsync(
        AccountSummary account,
        CancellationToken cancellationToken)
    {
        var token = CreateToken();
        var createdAtUtc = DateTime.UtcNow;
        var expiresAtUtc = createdAtUtc.Add(SessionLifetime);
        await accountStore.CreateSessionAsync(
            HashToken(token),
            account.UserId,
            createdAtUtc,
            expiresAtUtc,
            GetClientIpAddress(),
            cancellationToken);
        return new AuthenticationResponse(account, token, expiresAtUtc);
    }

    private async Task<StoredAccount?> AuthenticateAsync(
        string? token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 32 or > 256)
        {
            return null;
        }

        var userId = await accountStore.ResolveSessionAsync(
            HashToken(token),
            DateTime.UtcNow,
            GetClientIpAddress(),
            cancellationToken);
        if (userId is null)
        {
            return null;
        }

        var account = await accountStore.FindByIdAsync(userId, cancellationToken);
        return account is { Status: ActiveStatus, Deactivated: false } ? account : null;
    }

    private string? GetClientIpAddress()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return null;
        }

        return ClientRequestIdentity.GetClientIpAddress(httpContext);
    }

    private static string? NormalizePlayerName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = WhitespaceRegex().Replace(value.Trim(), " ");
        return normalized.Length is >= 3 and <= 24 && PlayerNameRegex().IsMatch(normalized)
            ? normalized
            : null;
    }

    private static string NormalizePlayerNameForLookup(string value) => value.ToUpperInvariant();

    private static string? NormalizeEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var address = new MailAddress(value.Trim()).Address;
            return address.Length <= 254 ? address.ToLowerInvariant() : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsValidPassword(string? value) =>
        value is not null && value.Length is >= MinimumPasswordLength and <= MaximumPasswordLength;

    private static string CreateToken() => Convert
        .ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string HashToken(string token) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^[\p{L}\p{N} _-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PlayerNameRegex();
}
