using System;

namespace FortuneForge.Games.Abstractions.Wallet;

/// <summary>Checks a wallet receipt against the exact operation the caller submitted.</summary>
public static class GameWalletReceiptVerifier
{
    public static bool MatchesSettlement(GameWalletSettlementReceipt receipt, string gameId, string accountId,
        GameWalletSettlementRequest request)
    {
        var id = request.Phase == GameWalletSettlementPhase.CompleteReservation
            ? GameWalletSettlementIdentity.CreateCompletionId(gameId, accountId, request.OperationId)
            : GameWalletSettlementIdentity.CreateId(gameId, accountId, request.OperationId);
        if (receipt.SettlementId != id ||
            !GameWalletBalanceRequirement.IsValid(request.Phase, request.MinimumBalanceBeforeCents) ||
            receipt.SettledAtUtc == default || receipt.SettledAtUtc.Offset != TimeSpan.Zero ||
            receipt.BalanceBeforeCents < 0 || receipt.BalanceAfterWagerCents < 0 || receipt.BalanceAfterCents < 0 ||
            receipt.LegacyFreeGamesRemaining < 0 ||
            (receipt.InsufficientLegacyFreeGames && (!request.SpendLegacyFreeGame || receipt.LegacyFreeGamesRemaining != 0)))
            return false;

        var declined = receipt.LegacyFreeGamesCountMismatch || receipt.InsufficientFunds || receipt.InsufficientLegacyFreeGames;
        if (receipt.LegacyFreeGamesCountMismatch)
        {
            if (receipt.InsufficientFunds || receipt.InsufficientLegacyFreeGames ||
                request.ExpectedLegacyFreeGamesRemaining is not { } mismatchedExpectation ||
                receipt.LegacyFreeGamesRemaining is not { } actual || actual == mismatchedExpectation)
                return false;
        }
        else if (receipt.InsufficientFunds != (receipt.BalanceBeforeCents <
                GameWalletBalanceRequirement.RequiredCents(request.WagerCents, request.MinimumBalanceBeforeCents)) ||
            (request.ExpectedLegacyFreeGamesRemaining is { } expected &&
                (receipt.LegacyFreeGamesRemaining != expected - (!declined && request.SpendLegacyFreeGame ? 1 : 0) ||
                 receipt.InsufficientLegacyFreeGames != (request.SpendLegacyFreeGame && expected == 0))))
        {
            return false;
        }

        if (declined)
            return receipt.BalanceBeforeCents == receipt.BalanceAfterWagerCents && receipt.BalanceBeforeCents == receipt.BalanceAfterCents &&
                receipt.WagerTransactionId is null && receipt.PayoutTransactionId is null && receipt.LegacyFreeGameTransactionId is null;

        return receipt.BalanceBeforeCents - receipt.BalanceAfterWagerCents == request.WagerCents &&
            receipt.BalanceAfterCents - receipt.BalanceAfterWagerCents == request.PayoutCents &&
            (request.WagerCents > 0 ? !string.IsNullOrWhiteSpace(receipt.WagerTransactionId) : receipt.WagerTransactionId is null) &&
            (request.PayoutCents > 0 ? !string.IsNullOrWhiteSpace(receipt.PayoutTransactionId) : receipt.PayoutTransactionId is null) &&
            (request.SpendLegacyFreeGame ? !string.IsNullOrWhiteSpace(receipt.LegacyFreeGameTransactionId) &&
                receipt.LegacyFreeGamesRemaining is not null : receipt.LegacyFreeGameTransactionId is null);
    }
}
