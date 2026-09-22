using System.Security.Cryptography;
using System.Text;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Arcade.Competition;

internal sealed record ArcadeCompetitionPayoutCreditResult(
    string TransactionId,
    long BalanceAfterCents,
    DateTimeOffset CreditedAtUtc,
    bool WasAlreadyCredited);

internal interface IArcadeCompetitionPayoutCreditor
{
    Task<ArcadeCompetitionPayoutCreditResult> CreditAsync(
        ArcadeCompetitionIdentity competition,
        ArcadeCompetitionCreditInstruction instruction,
        DateTimeOffset creditedAtUtc,
        CancellationToken cancellationToken);
}

/// <summary>Credits one validated competition instruction; it does not execute plans or change settlement state.</summary>
internal sealed class FirestoreArcadeCompetitionPayoutCreditor : IArcadeCompetitionPayoutCreditor
{
    private const string SlotsCreditsCurrencyId = "slotsCredits";
    private const string AvailableFractionalCentsField = "availableFractionalCents";
    private readonly FirestoreDb database;

    public FirestoreArcadeCompetitionPayoutCreditor(FirestoreDb database)
    {
        this.database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public Task<ArcadeCompetitionPayoutCreditResult> CreditAsync(
        ArcadeCompetitionIdentity competition,
        ArcadeCompetitionCreditInstruction instruction,
        DateTimeOffset creditedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(competition);
        ArgumentNullException.ThrowIfNull(instruction);
        if (creditedAtUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Competition payout credit times must be UTC.", nameof(creditedAtUtc));
        if (!ArcadeCompetitionPayoutPlan.HasValidIdempotencyKey(competition, instruction))
            throw new ArgumentException("The payout instruction idempotency key does not match its competition and credit data.", nameof(instruction));

        var transactionId = TransactionIdFor(instruction.IdempotencyKey);
        var balanceReference = database.Collection("userBalances")
            .Document($"{instruction.PlayerId}_{SlotsCreditsCurrencyId}");
        var ledgerReference = database.Collection("balanceTransactions").Document(transactionId);

        return database.RunTransactionAsync(async transaction =>
        {
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(balanceReference, cancellationToken),
                transaction.GetSnapshotAsync(ledgerReference, cancellationToken));
            var balanceSnapshot = snapshots[0];
            var ledgerSnapshot = snapshots[1];
            var availableCents = ReadBalanceCents(balanceSnapshot, instruction.PlayerId);

            if (ledgerSnapshot.Exists)
            {
                var existing = VerifyAndReadLedger(
                    ledgerSnapshot,
                    transactionId,
                    competition,
                    instruction);
                return existing with { WasAlreadyCredited = true };
            }

            var balanceAfterCents = checked(availableCents + instruction.AmountCents);
            transaction.Update(balanceReference, BalanceUpdate(balanceAfterCents, creditedAtUtc));
            transaction.Create(ledgerReference, LedgerData(
                transactionId,
                competition,
                instruction,
                balanceAfterCents,
                creditedAtUtc));
            return new ArcadeCompetitionPayoutCreditResult(
                transactionId,
                balanceAfterCents,
                creditedAtUtc,
                WasAlreadyCredited: false);
        }, cancellationToken: cancellationToken);
    }

    internal static string TransactionIdFor(string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("A payout idempotency key is required.", nameof(idempotencyKey));
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey));
        return $"arcade-competition-credit-{Convert.ToHexStringLower(digest)}";
    }

    private static Dictionary<string, object> BalanceUpdate(long cents, DateTimeOffset updatedAtUtc) => new()
    {
        ["available"] = cents / 100,
        [AvailableFractionalCentsField] = cents % 100,
        ["version"] = FieldValue.Increment(1),
        ["updatedAt"] = Timestamp.FromDateTime(updatedAtUtc.UtcDateTime),
    };

    private static Dictionary<string, object> LedgerData(
        string transactionId,
        ArcadeCompetitionIdentity competition,
        ArcadeCompetitionCreditInstruction instruction,
        long balanceAfterCents,
        DateTimeOffset creditedAtUtc) => new()
    {
        ["transactionId"] = transactionId,
        ["userId"] = instruction.PlayerId,
        ["currencyId"] = SlotsCreditsCurrencyId,
        ["amount"] = (double)(instruction.AmountCents / 100m),
        ["amountCents"] = instruction.AmountCents,
        ["balanceAfter"] = (double)(balanceAfterCents / 100m),
        ["balanceAfterCents"] = balanceAfterCents,
        ["type"] = "arcade-competition-payout",
        ["competitionId"] = competition.DocumentId,
        ["gameId"] = competition.GameId,
        ["windowKind"] = competition.WindowKind.ToString().ToLowerInvariant(),
        ["startsAt"] = Timestamp.FromDateTime(competition.StartsAtUtc.UtcDateTime),
        ["endsAt"] = Timestamp.FromDateTime(competition.EndsAtUtc.UtcDateTime),
        ["payoutKind"] = instruction.Kind.ToString().ToLowerInvariant(),
        ["placement"] = instruction.Placement is null ? null! : (long)instruction.Placement.Value,
        ["idempotencyKey"] = instruction.IdempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(creditedAtUtc.UtcDateTime),
        ["schemaVersion"] = 1L,
    };

    private static long ReadBalanceCents(DocumentSnapshot snapshot, string userId)
    {
        if (!snapshot.Exists ||
            !snapshot.TryGetValue<string>("userId", out var storedUserId) || storedUserId != userId ||
            !snapshot.TryGetValue<string>("currencyId", out var currencyId) || currencyId != SlotsCreditsCurrencyId ||
            !snapshot.TryGetValue<long>("available", out var available) || available < 0 ||
            !snapshot.TryGetValue<long>(AvailableFractionalCentsField, out var fractional) || fractional is < 0 or >= 100 ||
            !snapshot.TryGetValue<long>("reserved", out var reserved) || reserved < 0 ||
            !snapshot.TryGetValue<long>("version", out var version) || version < 1)
        {
            throw new InvalidOperationException("The payout recipient's slots credit balance is missing or invalid.");
        }

        return checked(available * 100 + fractional);
    }

    private static ArcadeCompetitionPayoutCreditResult VerifyAndReadLedger(
        DocumentSnapshot snapshot,
        string transactionId,
        ArcadeCompetitionIdentity competition,
        ArcadeCompetitionCreditInstruction instruction)
    {
        var expectedPlacement = instruction.Placement is null ? (long?)null : instruction.Placement.Value;
        if (!snapshot.TryGetValue<string>("transactionId", out var storedTransactionId) || storedTransactionId != transactionId ||
            !snapshot.TryGetValue<string>("userId", out var userId) || userId != instruction.PlayerId ||
            !snapshot.TryGetValue<string>("currencyId", out var currencyId) || currencyId != SlotsCreditsCurrencyId ||
            !snapshot.TryGetValue<long>("amountCents", out var amountCents) || amountCents != instruction.AmountCents ||
            !snapshot.TryGetValue<double>("amount", out var amount) || amount != (double)(instruction.AmountCents / 100m) ||
            !snapshot.TryGetValue<long>("balanceAfterCents", out var balanceAfterCents) || balanceAfterCents < instruction.AmountCents ||
            !snapshot.TryGetValue<double>("balanceAfter", out var balanceAfter) || balanceAfter != (double)(balanceAfterCents / 100m) ||
            !snapshot.TryGetValue<string>("type", out var type) || type != "arcade-competition-payout" ||
            !snapshot.TryGetValue<string>("competitionId", out var competitionId) || competitionId != competition.DocumentId ||
            !snapshot.TryGetValue<string>("gameId", out var gameId) || gameId != competition.GameId ||
            !snapshot.TryGetValue<string>("windowKind", out var windowKind) || windowKind != competition.WindowKind.ToString().ToLowerInvariant() ||
            !snapshot.TryGetValue<Timestamp>("startsAt", out var startsAt) || new DateTimeOffset(startsAt.ToDateTime()) != competition.StartsAtUtc ||
            !snapshot.TryGetValue<Timestamp>("endsAt", out var endsAt) || new DateTimeOffset(endsAt.ToDateTime()) != competition.EndsAtUtc ||
            !snapshot.TryGetValue<string>("payoutKind", out var payoutKind) || payoutKind != instruction.Kind.ToString().ToLowerInvariant() ||
            ReadOptionalPlacement(snapshot) != expectedPlacement ||
            !snapshot.TryGetValue<string>("idempotencyKey", out var idempotencyKey) || idempotencyKey != instruction.IdempotencyKey ||
            !snapshot.TryGetValue<Timestamp>("createdAt", out var createdAt) ||
            !snapshot.TryGetValue<long>("schemaVersion", out var schemaVersion) || schemaVersion != 1)
        {
            throw new InvalidOperationException("The existing arcade competition payout ledger record is conflicting or invalid.");
        }

        return new ArcadeCompetitionPayoutCreditResult(
            transactionId,
            balanceAfterCents,
            new DateTimeOffset(createdAt.ToDateTime()),
            WasAlreadyCredited: true);
    }

    private static long? ReadOptionalPlacement(DocumentSnapshot snapshot) =>
        snapshot.TryGetValue<long>("placement", out var placement) ? placement : null;
}
