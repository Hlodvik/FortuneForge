using FortuneForge.Server.Accounts.Models;
using Google.Cloud.Firestore;
using static FortuneForge.Server.Accounts.Storage.FirestoreWalletDocuments;

namespace FortuneForge.Server.Accounts.Storage;

internal sealed class FirestoreWalletCreditStore(FirestoreDb database) : IWalletCreditStore
{
    public Task<WalletCreditReceipt> CreditAsync(
        WalletCredit credit, DateTimeOffset creditedAtUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credit);
        ArgumentException.ThrowIfNullOrWhiteSpace(credit.TransactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(credit.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(credit.LedgerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(credit.IdempotencyKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(credit.AmountCents);
        if (creditedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Wallet credit times must be UTC.", nameof(creditedAtUtc));

        var ledgerIdentity = CreateLedgerIdentity(credit);
        var balance = Balance(database, credit.UserId, SlotsCreditsCurrencyId);
        var ledger = Transaction(database, credit.TransactionId);
        var creditedAt = Timestamp.FromDateTime(creditedAtUtc.UtcDateTime);
        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await transaction.GetAllSnapshotsAsync([balance, ledger], cancellationToken);
            var availableCents = ReadSlotsCreditCents(snapshots[0], credit.UserId);
            if (snapshots[1].Exists)
                return ReadReceipt(snapshots[1], credit, ledgerIdentity);

            var balanceAfterCents = checked(availableCents + credit.AmountCents);
            transaction.Update(balance, SlotsCreditUpdate(balanceAfterCents, creditedAt));
            transaction.Create(ledger, new Dictionary<string, object>(ledgerIdentity)
            {
                ["balanceAfter"] = (double)RandMoney.CentsToRand(balanceAfterCents),
                ["balanceAfterCents"] = balanceAfterCents,
                ["createdAt"] = creditedAt,
            });
            return new WalletCreditReceipt(credit.TransactionId, balanceAfterCents, creditedAtUtc, WasAlreadyCredited: false);
        }, cancellationToken: cancellationToken);
    }

    private static Dictionary<string, object> CreateLedgerIdentity(WalletCredit credit)
    {
        var fields = RandTransactionIdentity(
            credit.TransactionId, credit.UserId, credit.AmountCents, credit.LedgerType, credit.IdempotencyKey);
        fields.Add("amountCents", credit.AmountCents);
        fields.Add("schemaVersion", 1L);

        foreach (var (name, value) in credit.LedgerMetadata)
        {
            if (name is "balanceAfter" or "balanceAfterCents" or "createdAt")
                throw new ArgumentException($"'{name}' is a wallet ledger field.", nameof(credit));
            fields.Add(name, value switch
            {
                DateTimeOffset instant => Timestamp.FromDateTime(instant.UtcDateTime),
                string or long => value,
                null => null!,
                _ => throw new ArgumentException($"'{name}' has an unsupported wallet ledger value.", nameof(credit)),
            });
        }

        return fields;
    }

    private static WalletCreditReceipt ReadReceipt(
        DocumentSnapshot snapshot, WalletCredit credit, IReadOnlyDictionary<string, object> ledgerIdentity)
    {
        var fields = snapshot.ToDictionary();
        if (ledgerIdentity.Any(field => !Equals(fields.GetValueOrDefault(field.Key), field.Value)) ||
            fields.GetValueOrDefault("balanceAfterCents") is not long balanceAfterCents || balanceAfterCents < credit.AmountCents ||
            fields.GetValueOrDefault("balanceAfter") is not double balanceAfter || balanceAfter != (double)RandMoney.CentsToRand(balanceAfterCents) ||
            fields.GetValueOrDefault("createdAt") is not Timestamp createdAt)
        {
            throw new InvalidOperationException("The existing wallet credit ledger record is conflicting or invalid.");
        }

        return new(credit.TransactionId, balanceAfterCents, new DateTimeOffset(createdAt.ToDateTime()), WasAlreadyCredited: true);
    }
}
