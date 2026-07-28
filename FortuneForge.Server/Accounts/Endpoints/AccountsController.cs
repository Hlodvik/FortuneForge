using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Controllers;

[ApiController]
[Route("api/accounts")]
public sealed class AccountsController(
    AccountService accountService,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.AccountCreation)]
    [ProducesResponseType<CreateAccountResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await accountService.CreateAsync(request, cancellationToken);
        return result.Value is not null
            ? Created("/api/accounts/me", result.Value)
            : FromError(result.Error);
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.AccountAuthentication)]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await accountService.LoginAsync(request, cancellationToken);
        if (result.Value is null)
        {
            return FromError(result.Error);
        }

        AccountSessionCookie.Write(
            Response,
            result.Value.Token,
            result.Value.ExpiresAtUtc,
            !environment.IsDevelopment(),
            request.RemainLoggedIn);
        return Ok(result.Value);
    }

    [HttpPost("resend-verification")]
    [EnableRateLimiting(RateLimitPolicies.EmailVerification)]
    [ProducesResponseType<ResendVerificationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ResendVerification(
        [FromBody] ResendVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await accountService.ResendVerificationAsync(request, cancellationToken);
        return result.Value is not null ? Ok(result.Value) : FromError(result.Error);
    }

    [HttpGet("me")]
    [ProducesResponseType<AccountSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var result = await accountService.GetProfileAsync(SessionToken(), cancellationToken);
        return result.Value is not null ? Ok(result.Value) : FromError(result.Error);
    }

    [HttpPatch("me")]
    [ProducesResponseType<AccountSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await accountService.UpdateProfileAsync(
            SessionToken(),
            request,
            cancellationToken);
        return result.Value is not null ? Ok(result.Value) : FromError(result.Error);
    }

    [HttpPost("change-password")]
    [ProducesResponseType<AccountSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var result = await accountService.ChangePasswordAsync(
            SessionToken(),
            request,
            cancellationToken);
        return result.Value is not null ? Ok(result.Value) : FromError(result.Error);
    }

    [HttpGet("me/history")]
    [ProducesResponseType<SlotHistoryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var result = await accountService.GetSlotHistoryAsync(
            SessionToken(),
            limit,
            cancellationToken);
        return result.Value is not null ? Ok(result.Value) : FromError(result.Error);
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var result = await accountService.LogoutAsync(SessionToken(), cancellationToken);
        if (result.Value is null)
        {
            return FromError(result.Error);
        }

        AccountSessionCookie.Delete(Response, !environment.IsDevelopment());
        return NoContent();
    }

    [HttpDelete("me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(
        [FromBody] DeleteAccountRequest request,
        CancellationToken cancellationToken)
    {
        var result = await accountService.DeactivateAsync(
            SessionToken(),
            request,
            cancellationToken);
        if (result.Value is null)
        {
            return FromError(result.Error);
        }

        AccountSessionCookie.Delete(Response, !environment.IsDevelopment());
        return NoContent();
    }

    private string? SessionToken() => AccountSessionCookie.Read(Request);

    private ObjectResult FromError(AccountError error) => error switch
    {
        AccountError.InvalidPlayerName => Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid player name",
            detail: "Player names must be 3–24 characters and may contain letters, numbers, spaces, underscores, and hyphens."),
        AccountError.InvalidEmail => Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid email",
            detail: "Enter a valid email address."),
        AccountError.InvalidPassword => Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid password",
            detail: "Passwords must be between 8 and 128 characters."),
        AccountError.InvalidCredentials => Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Unable to sign in",
            detail: "The email or password was incorrect."),
        AccountError.Deactivated => Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Account deactivated",
            detail: "Your account has been deactivated."),
        AccountError.EmailNotVerified => Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Email verification required",
            detail: "Verify your email address before signing in."),
        AccountError.VerificationRateLimited => Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Verification email already sent",
            detail: "Wait a minute before requesting another verification email."),
        AccountError.VerificationServiceUnavailable => Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Verification service unavailable",
            detail: "Firebase could not send a verification email. Try again in a moment."),
        AccountError.Unauthorized => Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication required",
            detail: "Sign in again to continue."),
        AccountError.PlayerNameTaken => Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Player name unavailable",
            detail: "That player name is already in use."),
        AccountError.EmailTaken => Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Account already exists",
            detail: "An account already exists for that email address."),
        AccountError.AccountNotFound => Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Account not found"),
        _ => Problem(statusCode: StatusCodes.Status500InternalServerError)
    };
}
