using Google.Cloud.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed partial class FirestoreCompetitiveSolitaireStore
{
    private void WriteSecurityState(Transaction transaction, SolitaireMatch match,
        IReadOnlyList<SolitairePlayerState> players, DateTime nowUtc, string eventKind)
    {
        // Solitaire versions belong to seats. Preserve every seat's actual version in the
        // payload; their sum is only an ordering hint, not a fabricated match revision.
        FortuneForge.Server.SecurityAudit.GameSecurityJournal.Write(transaction, database, "solitaire", match.MatchId,
            players.Sum(player => (long)player.Version), nowUtc,
            new { match, players, versionScope = "sum-of-seat-versions" },
            players.Where(player => player.IsAccountBacked).Select(player => player.UserId), eventKind);
    }
}
