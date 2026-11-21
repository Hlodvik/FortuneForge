using System.Text.Json;
using System.Text.Json.Serialization;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.VideoPoker;

namespace FortuneForge.Server.Cards.VideoPoker;

internal sealed record VideoPokerPreparedOperation(
    string UserId, string RoundId, string IdempotencyKey, string Kind, string RequestSha256,
    VideoPokerRound Round, DateTimeOffset StartedAtUtc, DateTimeOffset OccurredAtUtc,
    GameRequestObservation? Observation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] VideoPokerFinancialSnapshot? PriorFinancialEvidence = null)
{
    internal long WagerCents => Kind == "deal" ? VideoPokerMoney.WagerCents(Round.CoinsWagered, Round.HandCount) : 0;
    internal long PayoutCents => Kind == "draw"
        ? checked(Round.Results.Sum(hand => hand.PaytableOutcome.CreditsWon) * VideoPokerMoney.CoinValueCents) : 0;
    internal string WalletOperationId => $"{VideoPokerBackend.GameId}-{RoundId}-{Kind}";
    internal string OutcomeSha256 => GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(this, RoundAuditCapture.Json));
    internal GameWalletSettlementRequest WalletRequest => new(WalletOperationId, OutcomeSha256, WagerCents, PayoutCents);

    internal static string RequestHash(string kind, string roundId, int coins, int hands, IReadOnlyList<int>? holds = null) =>
        GameSecurityEventDocuments.Hash(JsonSerializer.Serialize(new { kind, roundId, coins, hands, holds }, RoundAuditCapture.Json));
}
