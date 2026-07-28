using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    private Dictionary<string, object> CheckoutData(StoredPaymentCheckout checkout)
    {
        var data = new Dictionary<string, object>
        {
            ["checkoutId"] = checkout.CheckoutId,
            ["providerCheckoutId"] = checkout.ProviderCheckoutId,
            ["providerPathwayKey"] = checkout.ProviderPathwayKey ?? string.Empty,
            ["invoiceId"] = checkout.InvoiceId,
            ["userId"] = checkout.UserId,
            ["userReference"] = database.Collection("users").Document(checkout.UserId),
            ["idempotencyKey"] = checkout.IdempotencyKey,
            ["providerId"] = checkout.ProviderId,
            ["isMock"] = checkout.IsMock,
            ["market"] = checkout.Market.Code,
            ["marketName"] = checkout.Market.DisplayName,
            ["currency"] = checkout.Market.Currency,
            ["locale"] = checkout.Market.Locale,
            ["paymentMethodId"] = checkout.PaymentMethod.Id,
            ["paymentMethodName"] = checkout.PaymentMethod.DisplayName,
            ["paymentMethodType"] = checkout.PaymentMethod.Type,
            ["amount"] = checkout.Amount,
            ["amountMinor"] = checkout.AmountMinor,
            ["credits"] = checkout.Credits,
            ["customerFirstName"] = checkout.Customer.FirstName,
            ["customerLastName"] = checkout.Customer.LastName,
            ["customerEmail"] = checkout.Customer.Email,
            ["customerReference"] = checkout.Customer.CustomerReference,
            ["beneficiaryReference"] = checkout.Customer.BeneficiaryReference,
            ["payerAccountHolder"] = checkout.PayerBank.AccountHolder,
            ["payerBankName"] = checkout.PayerBank.BankName,
            ["payerAccountNumber"] = checkout.PayerBank.AccountNumber,
            ["payerBranchCode"] = checkout.PayerBank.BranchCode,
            ["payerAccountType"] = checkout.PayerBank.AccountType,
            ["status"] = checkout.Status,
            ["statusUpdatedAt"] = Timestamp.FromDateTime(checkout.StatusUpdatedAtUtc),
            ["createdAt"] = Timestamp.FromDateTime(checkout.CreatedAtUtc),
            ["expiresAt"] = Timestamp.FromDateTime(checkout.ExpiresAtUtc),
            ["providerSubmissionStatus"] = checkout.ProviderSubmissionStatus,
            ["providerSubmissionAttempt"] = checkout.ProviderSubmissionAttempt,
            ["notice"] = checkout.Notice
        };
        if (!string.IsNullOrWhiteSpace(checkout.ProviderSubmissionLeaseId))
        {
            data["providerSubmissionLeaseId"] = checkout.ProviderSubmissionLeaseId;
        }

        if (checkout.ProviderSubmissionLeaseUntilUtc is { } leaseUntilUtc)
        {
            data["providerSubmissionLeaseUntil"] = Timestamp.FromDateTime(leaseUntilUtc);
        }

        if (checkout.NextProviderSubmissionAtUtc is { } nextSubmissionAtUtc)
        {
            data["nextProviderSubmissionAt"] = Timestamp.FromDateTime(nextSubmissionAtUtc);
        }

        if (checkout.LastProviderSubmissionAtUtc is { } lastSubmissionAtUtc)
        {
            data["lastProviderSubmissionAt"] = Timestamp.FromDateTime(lastSubmissionAtUtc);
        }

        if (checkout.LastProviderSubmissionStatusCode is { } statusCode)
        {
            data["lastProviderSubmissionStatusCode"] = statusCode;
        }

        if (checkout.BankTransfer is not null)
        {
            data["bankName"] = checkout.BankTransfer.BankName;
            data["bankAccountName"] = checkout.BankTransfer.AccountName;
            data["bankAccountNumber"] = checkout.BankTransfer.AccountNumber;
            data["bankBranchCode"] = checkout.BankTransfer.BranchCode;
            data["bankReference"] = checkout.BankTransfer.Reference;
            data["bankInstructions"] = checkout.BankTransfer.Instructions;
        }

        return data;
    }
}
