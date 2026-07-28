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

}
