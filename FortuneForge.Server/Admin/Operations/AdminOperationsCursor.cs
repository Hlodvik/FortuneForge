using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FortuneForge.Server.Admin.Operations;

internal sealed class AdminOperationsCursor(string signingKey)
{
    private readonly byte[] key = Encoding.UTF8.GetBytes(signingKey);

    public string Encode(string operation, DateTime occurredAtUtc, string id)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(
            operation,
            occurredAtUtc.ToUniversalTime().Ticks,
            id));
        var signature = HMACSHA256.HashData(key, payload);
        var token = new byte[payload.Length + signature.Length];
        payload.CopyTo(token, 0);
        signature.CopyTo(token, payload.Length);
        return Base64UrlEncode(token);
    }

    public (DateTime OccurredAtUtc, string Id) Decode(string operation, string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1_024)
            throw new AdminOperationsQueryException("The cursor is invalid.");

        byte[] data;
        try { data = Base64UrlDecode(token); }
        catch (FormatException) { throw new AdminOperationsQueryException("The cursor is invalid."); }
        if (data.Length <= 32) throw new AdminOperationsQueryException("The cursor is invalid.");

        var payload = data.AsSpan(0, data.Length - 32);
        var suppliedSignature = data.AsSpan(data.Length - 32);
        var expectedSignature = HMACSHA256.HashData(key, payload);
        if (!CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            throw new AdminOperationsQueryException("The cursor is invalid.");

        CursorPayload? decoded;
        try { decoded = JsonSerializer.Deserialize<CursorPayload>(payload); }
        catch (JsonException) { throw new AdminOperationsQueryException("The cursor is invalid."); }
        if (decoded is null ||
            !string.Equals(decoded.Operation, operation, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(decoded.Id))
        {
            throw new AdminOperationsQueryException("The cursor is invalid.");
        }

        try { return (new DateTime(decoded.Ticks, DateTimeKind.Utc), decoded.Id); }
        catch (ArgumentOutOfRangeException)
        {
            throw new AdminOperationsQueryException("The cursor is invalid.");
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += new string('=', (4 - normalized.Length % 4) % 4);
        return Convert.FromBase64String(normalized);
    }

    private sealed record CursorPayload(string Operation, long Ticks, string Id);
}

internal sealed class AdminOperationsQueryException(string message) : Exception(message);
