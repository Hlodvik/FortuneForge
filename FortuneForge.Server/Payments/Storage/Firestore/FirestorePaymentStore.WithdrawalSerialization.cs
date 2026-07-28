using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Payments.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Payments.Storage;

internal sealed partial class FirestorePaymentStore
{
    private Dictionary<string, object> WithdrawalData(StoredPaymentWithdrawal withdrawal) => new()
    {
        ["withdrawalId"] = withdrawal.WithdrawalId,
        ["providerWithdrawalId"] = withdrawal.ProviderWithdrawalId,
        ["providerPathwayKey"] = withdrawal.ProviderPathwayKey ?? string.Empty,
        ["userId"] = withdrawal.UserId,
        ["userReference"] = database.Collection("users").Document(withdrawal.UserId),
        ["idempotencyKey"] = withdrawal.IdempotencyKey,
        ["providerId"] = withdrawal.ProviderId,
        ["isMock"] = withdrawal.IsMock,
        ["market"] = withdrawal.Market.Code,
        ["marketName"] = withdrawal.Market.DisplayName,
        ["currency"] = withdrawal.Market.Currency,
        ["locale"] = withdrawal.Market.Locale,
        ["amount"] = withdrawal.Amount,
        ["amountMinor"] = withdrawal.AmountMinor,
        ["creditsDebited"] = withdrawal.CreditsDebited,
        ["status"] = withdrawal.Status,
        ["statusUpdatedAt"] = Timestamp.FromDateTime(withdrawal.StatusUpdatedAtUtc),
        ["createdAt"] = Timestamp.FromDateTime(withdrawal.CreatedAtUtc),
        ["customerFirstName"] = withdrawal.Customer.FirstName,
        ["customerLastName"] = withdrawal.Customer.LastName,
        ["customerEmail"] = withdrawal.Customer.Email,
        ["customerReference"] = withdrawal.Customer.CustomerReference,
        ["beneficiaryReference"] = withdrawal.Customer.BeneficiaryReference,
        ["accountHolder"] = withdrawal.Bank.AccountHolder,
        ["bankName"] = withdrawal.Bank.BankName,
        ["bankAccountNumber"] = withdrawal.Bank.AccountNumber,
        ["bankBranchCode"] = withdrawal.Bank.BranchCode,
        ["bankAccountType"] = withdrawal.Bank.AccountType,
        ["notice"] = withdrawal.Notice
    };
}
