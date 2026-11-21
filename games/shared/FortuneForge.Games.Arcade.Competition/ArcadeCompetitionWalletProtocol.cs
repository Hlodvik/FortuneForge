using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Arcade.Competition;

namespace FortuneForge.Games.Arcade.Competition;

public sealed record ArcadeCompetitionEntryProof(bool Verified);

public sealed record ArcadeCompetitionPayoutCharge(ArcadeCompetitionIdentity Competition,
    ArcadeCompetitionCreditInstruction Instruction, DateTimeOffset CreditedAtUtc);

public static class ArcadeCompetitionWalletProtocol
{
    public const string EntryRoutePattern = "/internal/wallet/v1/games/{gameId}/arcade-entry";
    public const string EntryProofRoutePattern = "/internal/wallet/v1/games/{gameId}/arcade-entry-proof";
    public const string PayoutRoutePattern = "/internal/wallet/v1/games/{gameId}/arcade-payout";

    public static string EntryRoute(string gameId) => EntryRoutePattern.Replace("{gameId}", gameId, StringComparison.Ordinal);
    public static string EntryProofRoute(string gameId) => EntryProofRoutePattern.Replace("{gameId}", gameId, StringComparison.Ordinal);
    public static string PayoutRoute(string gameId) => PayoutRoutePattern.Replace("{gameId}", gameId, StringComparison.Ordinal);

    public static bool IsEntryCharge(string gameId, string actor, ArcadeCompetitionEntryCharge? charge) =>
        charge is not null && IsEntryDebit(gameId, actor, charge.Entry) && GameSecurityEventDocuments.IsEventId(charge.OutcomeSha256);

    public static bool IsEntryDebit(string gameId, string actor, ArcadeCompetitionEntryDebit? entry) =>
        entry is not null && GameWalletAuthentication.IsValidGameId(gameId) && entry.GameId == gameId &&
        !string.IsNullOrWhiteSpace(actor) && entry.PlayerId == actor && !string.IsNullOrWhiteSpace(entry.AttemptId) &&
        GameSecurityEventDocuments.IsEventId(entry.AttemptDocumentId) && entry.EntryFeeCents > 0 &&
        entry.EnteredAtUtc != default && entry.EnteredAtUtc.Offset == TimeSpan.Zero;

    public static bool IsPayoutCharge(string gameId, string actor, ArcadeCompetitionPayoutCharge? charge) =>
        charge?.Competition is { } competition && charge.Instruction is { } instruction &&
        GameWalletAuthentication.IsValidGameId(gameId) && competition.GameId == gameId &&
        !string.IsNullOrWhiteSpace(actor) && instruction.PlayerId == actor &&
        Enum.IsDefined(competition.WindowKind) && Enum.IsDefined(instruction.Kind) &&
        charge.CreditedAtUtc >= competition.EndsAtUtc && charge.CreditedAtUtc.Offset == TimeSpan.Zero &&
        ArcadeCompetitionPayoutPlan.HasValidIdempotencyKey(competition, instruction);

    public static bool MatchesEntryReceipt(ArcadeCompetitionEntryDebit debit, GameWalletSettlementReceipt receipt)
    {
        var expectedId = GameWalletSettlementIdentity.CreateId(debit.GameId, debit.PlayerId, debit.AttemptDocumentId);
        var enoughCredits = receipt.BalanceBeforeCents >= debit.EntryFeeCents;
        var after = receipt.BalanceBeforeCents - (enoughCredits ? debit.EntryFeeCents : 0L);
        return receipt.SettlementId == expectedId && receipt.InsufficientFunds != enoughCredits &&
            receipt.BalanceBeforeCents >= 0 && receipt.BalanceAfterCents == after && receipt.BalanceAfterWagerCents == after &&
            receipt.WagerTransactionId == (enoughCredits ? debit.TransactionId : null) && receipt.PayoutTransactionId is null &&
            receipt.LegacyFreeGameTransactionId is null && receipt.LegacyFreeGamesRemaining is null &&
            !receipt.InsufficientLegacyFreeGames && !receipt.LegacyFreeGamesCountMismatch &&
            receipt.SettledAtUtc != default && receipt.SettledAtUtc.Offset == TimeSpan.Zero;
    }

    public static bool MatchesPayoutReceipt(ArcadeCompetitionCreditInstruction instruction,
        ArcadeCompetitionPayoutCreditResult receipt) =>
        receipt.TransactionId == ArcadeCompetitionPayoutPlan.WalletTransactionId(instruction.IdempotencyKey) &&
        receipt.BalanceAfterCents >= instruction.AmountCents && receipt.CreditedAtUtc != default && receipt.CreditedAtUtc.Offset == TimeSpan.Zero;
}
