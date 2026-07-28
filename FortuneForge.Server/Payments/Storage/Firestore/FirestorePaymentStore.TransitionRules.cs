using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    private static bool CanTransition(string current, string next) => current switch
    {
        "received" => next is "processing" or "completed" or "failed" or "expired",
        "processing" => next is "completed" or "failed" or "expired",
        _ => false
    };

    private static bool Matches(StoredPaymentCheckout existing, StoredPaymentCheckout proposed) =>
        string.Equals(existing.Market.Code, proposed.Market.Code, StringComparison.Ordinal) &&
        string.Equals(existing.Market.Currency, proposed.Market.Currency, StringComparison.Ordinal) &&
        string.Equals(existing.PaymentMethod.Id, proposed.PaymentMethod.Id, StringComparison.Ordinal) &&
        existing.Amount == proposed.Amount &&
        existing.Credits == proposed.Credits &&
        string.Equals(existing.Customer.Email, proposed.Customer.Email, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.Customer.FirstName, proposed.Customer.FirstName, StringComparison.Ordinal) &&
        string.Equals(existing.Customer.LastName, proposed.Customer.LastName, StringComparison.Ordinal) &&
        string.Equals(existing.PayerBank.AccountHolder, proposed.PayerBank.AccountHolder, StringComparison.Ordinal) &&
        string.Equals(existing.PayerBank.BankName, proposed.PayerBank.BankName, StringComparison.Ordinal) &&
        string.Equals(existing.PayerBank.AccountNumber, proposed.PayerBank.AccountNumber, StringComparison.Ordinal) &&
        string.Equals(existing.PayerBank.BranchCode, proposed.PayerBank.BranchCode, StringComparison.Ordinal) &&
        string.Equals(existing.PayerBank.AccountType, proposed.PayerBank.AccountType, StringComparison.Ordinal);

    private static bool Matches(StoredPaymentWithdrawal existing, StoredPaymentWithdrawal proposed) =>
        string.Equals(existing.Market.Code, proposed.Market.Code, StringComparison.Ordinal) &&
        string.Equals(existing.Market.Currency, proposed.Market.Currency, StringComparison.Ordinal) &&
        existing.Amount == proposed.Amount &&
        existing.CreditsDebited == proposed.CreditsDebited &&
        string.Equals(existing.Customer.Email, proposed.Customer.Email, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.Customer.FirstName, proposed.Customer.FirstName, StringComparison.Ordinal) &&
        string.Equals(existing.Customer.LastName, proposed.Customer.LastName, StringComparison.Ordinal) &&
        string.Equals(existing.Bank.AccountHolder, proposed.Bank.AccountHolder, StringComparison.Ordinal) &&
        string.Equals(existing.Bank.BankName, proposed.Bank.BankName, StringComparison.Ordinal) &&
        string.Equals(existing.Bank.AccountNumber, proposed.Bank.AccountNumber, StringComparison.Ordinal) &&
        string.Equals(existing.Bank.BranchCode, proposed.Bank.BranchCode, StringComparison.Ordinal) &&
        string.Equals(existing.Bank.AccountType, proposed.Bank.AccountType, StringComparison.Ordinal);

    private static string NormalizeWithdrawalStatus(string? status, string fallback) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "pending" => "pending",
            "processing" => "processing",
            "completed" => "completed",
            "rejected" => "failed",
            "reversed" => "failed",
            "failed" => "failed",
            _ => fallback
        };
}
