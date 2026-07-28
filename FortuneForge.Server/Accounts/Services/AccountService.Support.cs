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
