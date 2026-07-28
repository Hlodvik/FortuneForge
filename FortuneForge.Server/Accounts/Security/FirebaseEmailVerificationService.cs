using System.Net.Http.Json;
using System.Text.Json;
using FirebaseAdmin.Auth;
using FortuneForge.Server.Accounts.Models;

namespace FortuneForge.Server.Accounts.Security;

public sealed record FirebaseUserRegistration(string UserId, string IdToken);

public sealed class FirebaseEmailVerificationService(
    FirebaseAuth firebaseAuth,
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<FirebaseEmailVerificationService> logger)
{
    private readonly string apiKey = configuration["FirebaseAuthentication:WebApiKey"]
        ?? throw new InvalidOperationException("FirebaseAuthentication:WebApiKey is required.");
    private readonly string continueUrl = configuration["FirebaseAuthentication:VerificationContinueUrl"]
        ?? throw new InvalidOperationException(
            "FirebaseAuthentication:VerificationContinueUrl is required.");

    public async Task<AccountResult<FirebaseUserRegistration>> CreateUserAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={apiKey}",
            new
            {
                email,
                password,
                returnSecureToken = true
            },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadFirebaseErrorAsync(response, cancellationToken);
            logger.LogWarning("Firebase account creation failed with {Error}.", error);
            return AccountResult<FirebaseUserRegistration>.Failure(error switch
            {
                "EMAIL_EXISTS" => AccountError.EmailTaken,
                "WEAK_PASSWORD" => AccountError.InvalidPassword,
                _ => AccountError.VerificationServiceUnavailable
            });
        }

        var payload = await response.Content.ReadFromJsonAsync<FirebaseSignInResponse>(
            cancellationToken: cancellationToken);
        return payload is not null &&
               !string.IsNullOrWhiteSpace(payload.LocalId) &&
               !string.IsNullOrWhiteSpace(payload.IdToken)
            ? AccountResult<FirebaseUserRegistration>.Success(
                new FirebaseUserRegistration(payload.LocalId, payload.IdToken))
            : AccountResult<FirebaseUserRegistration>.Failure(
                AccountError.VerificationServiceUnavailable);
    }

    public async Task<bool> SendVerificationEmailAsync(
        string idToken,
        CancellationToken cancellationToken)
    {
        return await SendVerificationRequestAsync(idToken, cancellationToken) is null;
    }

    private async Task<string?> SendVerificationRequestAsync(
        string idToken,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={apiKey}",
            new
            {
                requestType = "VERIFY_EMAIL",
                idToken,
                continueUrl
            },
            cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return null;
        }

        var error = await ReadFirebaseErrorAsync(response, cancellationToken);
        logger.LogWarning(
            "Firebase verification email delivery failed with {Error}.",
            error);
        return error;
    }

    public async Task<AccountError> ResendVerificationEmailAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        using var signInResponse = await httpClient.PostAsJsonAsync(
            $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={apiKey}",
            new
            {
                email,
                password,
                returnSecureToken = true
            },
            cancellationToken);
        if (!signInResponse.IsSuccessStatusCode)
        {
            var error = await ReadFirebaseErrorAsync(signInResponse, cancellationToken);
            return error is "INVALID_LOGIN_CREDENTIALS" or "EMAIL_NOT_FOUND" or "INVALID_PASSWORD"
                ? AccountError.InvalidCredentials
                : AccountError.VerificationServiceUnavailable;
        }

        var signIn = await signInResponse.Content.ReadFromJsonAsync<FirebaseSignInResponse>(
            cancellationToken: cancellationToken);
        if (signIn is null || string.IsNullOrWhiteSpace(signIn.IdToken))
        {
            return AccountError.VerificationServiceUnavailable;
        }

        var sendError = await SendVerificationRequestAsync(signIn.IdToken, cancellationToken);
        if (sendError is null)
        {
            return AccountError.None;
        }

        return sendError.Contains("TOO_MANY_ATTEMPTS", StringComparison.OrdinalIgnoreCase)
            ? AccountError.VerificationRateLimited
            : AccountError.VerificationServiceUnavailable;
    }

    public async Task<bool> IsEmailVerifiedAsync(
        string email,
        CancellationToken cancellationToken)
    {
        try
        {
            var firebaseUser = await firebaseAuth.GetUserByEmailAsync(email, cancellationToken);
            return firebaseUser.EmailVerified;
        }
        catch (FirebaseAuthException exception)
        {
            logger.LogWarning(
                exception,
                "Firebase could not confirm verification for {Email}.",
                email);
            return false;
        }
    }

    public async Task DeleteUserIfPresentAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            await firebaseAuth.DeleteUserAsync(userId, cancellationToken);
        }
        catch (FirebaseAuthException exception)
        {
            logger.LogWarning(
                exception,
                "Firebase rollback could not remove user {UserId}.",
                userId);
        }
    }

    private static async Task<string> ReadFirebaseErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            using var payload = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            return payload.RootElement
                .GetProperty("error")
                .GetProperty("message")
                .GetString() ?? "UNKNOWN";
        }
        catch (JsonException)
        {
            return "UNKNOWN";
        }
    }

    private sealed record FirebaseSignInResponse(string LocalId, string IdToken);
}
