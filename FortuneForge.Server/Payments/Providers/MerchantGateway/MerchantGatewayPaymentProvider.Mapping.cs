using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Storage;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Payments.Providers;

internal sealed partial class MerchantGatewayPaymentProvider
{
    private StoredPaymentCheckout CreateLocalCheckoutAttempt(
        PaymentCheckoutDraft draft,
        MerchantGatewayPathwayResponse? pathway)
    {
        var providerPathwayKey = NormalizePathwayKey(pathway?.Key);
        return new StoredPaymentCheckout(
            CreateLocalCheckoutId(draft.InvoiceId),
            string.Empty,
            providerPathwayKey,
            draft.InvoiceId,
            draft.UserId,
            draft.IdempotencyKey,
            Id,
            false,
            draft.Market,
            draft.PaymentMethod,
            draft.Amount,
            draft.AmountMinor,
            draft.Credits,
            "received",
            draft.CreatedAtUtc,
            draft.CreatedAtUtc,
            draft.ExpiresAtUtc,
            null,
            null,
            null,
            draft.Customer,
            draft.PayerBank,
            CreateBankTransfer(pathway, draft),
            "Payment invoice was prepared locally. Rand is added only after the invoice is marked completed.");
    }

    private static string CreateLocalCheckoutId(string invoiceId) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(invoiceId)))[..32];

    private static BankTransferInstructions? CreateBankTransfer(
        MerchantGatewayPathwayResponse? pathway,
        PaymentCheckoutDraft draft)
    {
        if (pathway is null ||
            string.IsNullOrWhiteSpace(pathway.Bank) ||
            string.IsNullOrWhiteSpace(pathway.AccountHolder) ||
            string.IsNullOrWhiteSpace(pathway.AccountNumber))
        {
            return null;
        }

        var reference = !string.IsNullOrWhiteSpace(draft.Customer.CustomerReference)
            ? draft.Customer.CustomerReference
            : !string.IsNullOrWhiteSpace(draft.Customer.BeneficiaryReference)
                ? draft.Customer.BeneficiaryReference
                : draft.InvoiceId;
        return new BankTransferInstructions(
            pathway.Bank,
            pathway.AccountHolder,
            pathway.AccountNumber,
            string.IsNullOrWhiteSpace(pathway.BranchCode) ? "Not supplied" : pathway.BranchCode,
            reference,
            $"Transfer exactly {draft.Amount.ToString(CultureInfo.InvariantCulture)} {draft.Market.Currency} and use {reference} as the payment reference.");
    }

    private static PaymentError MapProviderError(HttpStatusCode statusCode, string? responseBody = null)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return PaymentError.ProviderAuthenticationFailed;
        }

        if (statusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            return responseBody?.Contains("pathwayKey", StringComparison.OrdinalIgnoreCase) == true
                ? PaymentError.PaymentPathwayUnavailable
                : PaymentError.ProviderRejected;
        }

        return PaymentError.ProviderUnavailable;
    }

    private static bool IsAuthoritativeWithdrawalCreateRejection(
        HttpStatusCode statusCode,
        string? responseBody,
        bool isUncertainReplay)
    {
        if (isUncertainReplay)
        {
            return false;
        }

        if (statusCode == HttpStatusCode.Conflict)
        {
            return IsKnownNoCreateConflict(responseBody);
        }

        return statusCode is HttpStatusCode.BadRequest or
            HttpStatusCode.Unauthorized or
            HttpStatusCode.Forbidden or
            HttpStatusCode.UnprocessableEntity;
    }

    private static bool IsKnownNoCreateConflict(string? responseBody) =>
        responseBody?.Contains("no-create", StringComparison.OrdinalIgnoreCase) == true;

    private static string? NormalizePathwayKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsUsablePathwayKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.Trim().StartsWith("unconfigured-", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    private static bool MatchesLocalInvoice(
        StoredPaymentCheckout checkout,
        MerchantGatewayInvoiceResponse remote) =>
        remote.Id.ToString("N").Equals(checkout.ProviderCheckoutId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(remote.TheirNumber, checkout.InvoiceId, StringComparison.Ordinal) &&
        (string.IsNullOrWhiteSpace(remote.CustomerReference) ||
            string.Equals(remote.CustomerReference, checkout.Customer.CustomerReference, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(remote.CustomerReference, checkout.UserId, StringComparison.Ordinal)) &&
        remote.Amount == checkout.Amount &&
        string.Equals(remote.Currency, checkout.Market.Currency, StringComparison.OrdinalIgnoreCase);

    private static string MapStatus(string? status, string fallback) => status?.Trim().ToLowerInvariant() switch
    {
        "pending" => "received",
        "processing" => "processing",
        "completed" => "completed",
        "cancelled" => "failed",
        _ => fallback
    };

    private static bool SatisfiesExpectedStatus(string actualStatus, string expectedStatus) =>
        expectedStatus switch
        {
            "received" => actualStatus is "received" or "processing" or "completed" or "failed" or "expired",
            "processing" => actualStatus is "processing" or "completed",
            "completed" => actualStatus is "completed",
            "failed" => actualStatus is "failed",
            "expired" => actualStatus is "expired",
            _ => false
        };

    private static string MapWithdrawalStatus(string? status, string fallback) =>
        WithdrawalStatusProjection.NormalizeProviderStatus(status) ?? fallback;
}
