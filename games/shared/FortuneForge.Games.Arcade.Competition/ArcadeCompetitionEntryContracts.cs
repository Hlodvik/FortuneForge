using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Server.Arcade.Competition;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Arcade.Competition;

public interface IArcadeCompetitionRunAdapter<TRun> where TRun : class
{
    DocumentReference RunDocument(FirestoreDb database, ArcadeCompetitionAttemptRecord attempt);
    TRun PrepareRun(ArcadeCompetitionAttemptRecord attempt);
    TRun ReadRun(DocumentSnapshot snapshot, ArcadeCompetitionAttemptRecord attempt);
    void WriteRun(Transaction transaction, FirestoreDb database, DocumentReference document,
        ArcadeCompetitionAttemptRecord attempt, TRun run, string entryLedgerId);
}

public sealed record ArcadeCompetitionPaidEntry<TRun>(ArcadeCompetitionAttemptRecord Attempt, TRun Run,
    bool WasAlreadyRecorded) where TRun : class;

public sealed record ArcadeCompetitionRunCompletion<TResult>(long Score,
    Func<ArcadeCompetitionAttemptRecord, TResult> Result,
    Action<Transaction, DocumentReference, ArcadeCompetitionAttemptRecord>? WriteRun = null);

public sealed record ArcadeCompetitionEntryDebit(string GameId, string PlayerId, string AttemptId,
    string AttemptDocumentId, long EntryFeeCents, DateTimeOffset EnteredAtUtc)
{
    public string TransactionId => $"arcade-competition-{AttemptDocumentId}-entry";

    internal static ArcadeCompetitionEntryDebit From(ArcadeCompetitionAttemptRecord attempt) => new(
        attempt.Competition.GameId, attempt.PlayerId, attempt.AttemptId,
        attempt.DocumentId, attempt.EntryFeeCents, attempt.EnteredAtUtc);
}

public sealed record ArcadeCompetitionEntryCharge(ArcadeCompetitionEntryDebit Entry, string OutcomeSha256);

public interface IArcadeCompetitionEntryWallet
{
    Task<GameWalletSettlementReceipt> ChargeAsync(ArcadeCompetitionEntryCharge charge, CancellationToken cancellationToken);
    Task<bool> VerifyLegacyChargeAsync(ArcadeCompetitionEntryDebit entry, CancellationToken cancellationToken);
}

public sealed class ArcadeCompetitionEntryConflictException(string message) : InvalidOperationException(message);

public sealed class ArcadeCompetitionEntryPausedException() : InvalidOperationException(
    "New arcade competition entries are temporarily paused.");

public sealed class ArcadeCompetitionEntryPendingException() : InvalidOperationException(
    "The arcade competition has unfinished entry charges. Retry settlement after their receipts are confirmed.");
