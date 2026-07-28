using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Providers;
using FortuneForge.Server.Payments.Storage;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Payments;

public sealed partial class PaymentWebhookService
{
    private bool HasValidSignature(
        long timestamp,
        Guid eventId,
        ReadOnlySpan<byte> body,
        ReadOnlySpan<byte> providedDigest)
    {
        var valid = false;
        foreach (var secret in _options.WebhookSigningSecrets)
        {
            var expectedDigest = CreateDigest(secret, timestamp, eventId, body);
            valid |= CryptographicOperations.FixedTimeEquals(expectedDigest, providedDigest);
        }

        return valid;
    }

    private static byte[] CreateDigest(
        string signingSecret,
        long timestamp,
        Guid eventId,
        ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{timestamp}.{eventId:D}."));
        var input = new byte[prefix.Length + body.Length];
        prefix.CopyTo(input, 0);
        body.CopyTo(input.AsSpan(prefix.Length));
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingSecret), input);
    }

    private static bool TryDecodeSignature(string value, out byte[] digest)
    {
        digest = [];
        if (!value.StartsWith("v1=", StringComparison.Ordinal) || value.Length != 67)
        {
            return false;
        }

        try
        {
            digest = Convert.FromHexString(value.AsSpan(3));
            return digest.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryReadGuid(JsonElement element, string name, out Guid value)
    {
        value = Guid.Empty;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String &&
                Guid.TryParse(property.Value.GetString(), out value) &&
                value != Guid.Empty)
            {
                return true;
            }
        }

        value = Guid.Empty;
        return false;
    }

    private static bool HasMultipleValues(string value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains(',', StringComparison.Ordinal);

    private sealed record MerchantGatewayWebhookEnvelope(
        Guid EventId,
        string Type,
        DateTimeOffset OccurredAtUtc,
        JsonElement Data);
}
