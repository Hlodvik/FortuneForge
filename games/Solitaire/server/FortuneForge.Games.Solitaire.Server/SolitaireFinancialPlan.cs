using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Firestore;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed record SolitaireFrozenWrite(string Path, string Mode, string Fields);
internal sealed record SolitaireFrozenJournal(SolitaireMatch Match, SolitairePlayerState[] Players, DateTime OccurredAtUtc, string Kind);
internal sealed record SolitaireAuditInvalidation(string Path, DateTime OccurredAtUtc);
internal sealed record SolitaireFinancialPlan(string Id, string AccountId, string Key, string Operation, string Target,
    string Detail, DateTime PreparedAtUtc, long WagerCents, long PayoutCents, SolitaireFrozenWrite[] Writes,
    SolitaireFrozenJournal[] Journals, SolitaireAuditInvalidation[] Invalidations, GameRequestObservation? Observation,
    SolitaireCompletionReference? Completion = null);
internal sealed record SolitaireFinancialState(SolitaireFinancialPlan Plan, string Digest, string Stage, GameWalletBatchReceipt? Wallet);
internal sealed class SolitaireFinancialPendingException() : Exception("The original Solitaire wallet action must be recovered before the game can continue.");
internal sealed class SolitaireAdmissionPausedException() : Exception("Competitive Solitaire is not accepting new paid entries.");

/// <summary>Only game-owned state is frozen here. Account effects go exclusively through the signed wallet.</summary>
internal sealed class SolitaireFinancialBuffer
{
    private readonly List<SolitaireFrozenWrite> writes = [];
    private readonly List<SolitaireFrozenJournal> journals = [];
    private readonly List<SolitaireAuditInvalidation> invalidations = [];
    internal long WagerCents { get; private set; }
    internal long PayoutCents { get; private set; }
    private SolitaireCompletionReference? completion;

    internal void Debit(long cents) => WagerCents = checked(WagerCents + cents);
    internal void Credit(long cents) => PayoutCents = checked(PayoutCents + cents);
    internal void Create(DocumentReference reference, Dictionary<string, object> fields) => Add(reference, "create", fields);
    internal void Update(DocumentReference reference, Dictionary<string, object> fields) => Add(reference, "update", fields);
    internal void Set(DocumentReference reference, Dictionary<string, object> fields, SetOptions? options = null) =>
        Add(reference, options == SetOptions.MergeAll ? "merge" : "set", fields);
    internal void Journal(SolitaireMatch match, IEnumerable<SolitairePlayerState> players, DateTime now, string kind) =>
        journals.Add(new(match, players.ToArray(), now, kind));
    internal void InvalidateAudit(DocumentReference reference, DateTime now) => invalidations.Add(new(RelativeRootDocument(reference), now));
    internal void AnchorClaim(string evidenceId, string? evidenceSha256) => completion = new(evidenceId, evidenceSha256);

    internal SolitaireFinancialPlan Freeze(string id, string account, string key, string operation, string target,
        string detail, DateTime now, GameRequestObservation? observation)
    {
        foreach (var journal in journals)
            _ = FirestoreCompetitiveSolitaireStore.CaptureSecurityState(journal.Match, journal.Players,
                journal.OccurredAtUtc, journal.Kind);
        return new(id, account, key, operation, target, detail, now, WagerCents, PayoutCents,
            writes.ToArray(), journals.ToArray(), invalidations.ToArray(), observation, completion);
    }

    private void Add(DocumentReference reference, string mode, Dictionary<string, object> fields)
    {
        if (reference.Parent.Id is "userBalances" or "balanceTransactions")
            throw new InvalidOperationException("A Solitaire domain plan cannot write platform wallet documents.");
        writes.Add(new(RelativeRootDocument(reference), mode, FirestoreFieldCodec.Serialize(fields)));
    }

    private static string RelativeRootDocument(DocumentReference reference) => reference.Parent.Parent is null
        ? reference.Parent.Id + "/" + reference.Id
        : throw new InvalidOperationException("A Solitaire financial action can freeze only its established root document collections.");
}
