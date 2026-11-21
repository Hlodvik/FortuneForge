using Google.Cloud.Firestore;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Firestore;

namespace FortuneForge.Server.Cards.Solitaire;

internal sealed partial class FirestoreCompetitiveSolitaireStore
{
    private void WriteSecurityState(Transaction transaction, SolitaireMatch match,
        IReadOnlyList<SolitairePlayerState> players, DateTime nowUtc, string eventKind)
    {
        FirestoreGameAudit.WriteObservation(transaction, database, CaptureSecurityState(match, players, nowUtc, eventKind));
        foreach (var player in players.Where(player => player.IsAccountBacked &&
            (player.Game.Moves > 0 || eventKind == "human-entry")))
            FirestoreGameAudit.WriteAcceptedParticipation(transaction, database, ComputeGameId, player.UserId, nowUtc);
    }

    // Seat versions remain authoritative; their sum is only a journal ordering hint.
    internal static GameSecurityEventDocument CaptureSecurityState(SolitaireMatch match,
        IReadOnlyList<SolitairePlayerState> players, DateTime nowUtc, string eventKind) =>
        GameSecurityEventDocuments.Capture(ComputeGameId, match.MatchId,
            players.Sum(player => (long)player.Version), nowUtc,
            new { match, players, versionScope = "sum-of-seat-versions" },
            players.Where(player => player.IsAccountBacked).Select(player => player.UserId), eventKind);
}
