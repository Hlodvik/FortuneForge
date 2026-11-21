using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;

namespace FortuneForge.Server.Accounts.Storage;

internal sealed record WalletRoundSettlement(
    string GameId,
    string UserId,
    string OperationId,
    string OutcomeSha256,
    long WagerCents,
    long PayoutCents,
    bool SpendLegacyFreeGame = false,
    bool IncludeLegacyFreeGameBalance = false,
    string? RequestBodySha256 = null,
    long? ExpectedLegacyFreeGamesRemaining = null,
    bool AllowCashWagerWithLegacyFreeGame = false,
    GameWalletSettlementPhase Phase = GameWalletSettlementPhase.Direct,
    string? ReservationSha256 = null,
    long? MinimumBalanceBeforeCents = null)
{
    internal static WalletRoundSettlement FromDirectRequest(string gameId, string accountId, GameWalletSettlementRequest request)
    {
        if (request is not { Phase: GameWalletSettlementPhase.Direct, SpendLegacyFreeGame: false,
            ExpectedLegacyFreeGamesRemaining: null, ReservationSha256: null } ||
            !GameWalletBalanceRequirement.IsValid(request.Phase, request.MinimumBalanceBeforeCents))
            throw new ArgumentException("A direct cash wallet settlement with a valid balance requirement is required.", nameof(request));

        var body = JsonSerializer.SerializeToUtf8Bytes(request, GameWalletAuthentication.WireJson);
        return new(gameId, accountId, request.OperationId, request.OutcomeSha256, request.WagerCents, request.PayoutCents,
            RequestBodySha256: Convert.ToHexString(SHA256.HashData(body)),
            MinimumBalanceBeforeCents: request.MinimumBalanceBeforeCents);
    }
}

internal sealed record WalletRoundSettlementReceipt(
    string SettlementId,
    string? WagerTransactionId,
    string? PayoutTransactionId,
    long BalanceBeforeCents,
    long BalanceAfterWagerCents,
    long BalanceAfterCents,
    DateTimeOffset SettledAtUtc,
    bool WasAlreadySettled,
    bool InsufficientFunds,
    bool InsufficientLegacyFreeGames = false,
    string? LegacyFreeGameTransactionId = null,
    long? LegacyFreeGamesRemaining = null,
    bool LegacyFreeGamesCountMismatch = false)
{
    internal GameWalletSettlementReceipt ToWireReceipt() => new(SettlementId, WagerTransactionId,
        PayoutTransactionId, BalanceBeforeCents, BalanceAfterWagerCents, BalanceAfterCents, SettledAtUtc,
        WasAlreadySettled, InsufficientFunds, InsufficientLegacyFreeGames, LegacyFreeGameTransactionId,
        LegacyFreeGamesRemaining, LegacyFreeGamesCountMismatch);
}

internal sealed class WalletRoundSettlementConflictException() : InvalidOperationException(
    "The wallet operation was already used for a different outcome or amounts.");

internal interface IWalletRoundSettlementStore
{
    Task<WalletRoundSettlementReceipt> SettleAsync(
        WalletRoundSettlement settlement, DateTimeOffset settledAtUtc, CancellationToken cancellationToken,
        WalletRoundLedgerLayout? ledgerLayout = null);
}
