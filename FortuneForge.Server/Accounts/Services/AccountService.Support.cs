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
    private static readonly object RequestAuthenticationKey = new();
    private sealed record RequestAuthentication(string Token, StoredAccount Account);
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

        // Middleware and the endpoint authenticate the same cookie. Reuse only
        // within this request, never across requests or for a different token.
        var context = httpContextAccessor.HttpContext;
        if (context?.Items[RequestAuthenticationKey] is RequestAuthentication saved && saved.Token == token)
            return saved.Account;

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
        if (account is not { Status: ActiveStatus, Deactivated: false }) return null;
        if (context is not null) context.Items[RequestAuthenticationKey] = new RequestAuthentication(token, account);
        return account;
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
        AccountPasswordPolicy.IsValidNewPassword(value);

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
