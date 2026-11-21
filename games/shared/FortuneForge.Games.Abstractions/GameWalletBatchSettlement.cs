using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace FortuneForge.Games.Abstractions.Wallet;

public sealed record GameWalletBatchRequest(string OperationId, string OutcomeSha256,
    IReadOnlyList<GameWalletBatchEffect> Effects);

public sealed record GameWalletBatchEffect(string AccountId, long WagerCents, long PayoutCents,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? MinimumBalanceBeforeCents = null);

public sealed record GameWalletBatchReceipt(string BatchId, string ActorAccountId,
    IReadOnlyList<GameWalletBatchAccountReceipt> Accounts, DateTimeOffset SettledAtUtc, bool WasAlreadySettled,
    bool BatchDeclined);

public sealed record GameWalletBatchAccountReceipt(string AccountId, long BalanceBeforeCents,
    long BalanceAfterCents, bool InsufficientFunds, string? WagerTransactionId, string? PayoutTransactionId);

public interface IGameWalletBatchClient
{
    Task<GameWalletBatchReceipt> SettleBatchAsync(string actorAccountId, GameWalletBatchRequest request,
        CancellationToken cancellationToken);
}

public static class GameWalletBatchIdentity
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string CreateId(string gameId, string operationId) => $"wallet-batch-{Hash($"{gameId}\n{operationId}")}";

    public static string AccountLedgerPrefix(string batchId, string accountId) => $"{batchId}-{Hash(accountId)}";

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(value)));
}

public static class GameWalletBatchVerifier
{
    public const int MaximumAccounts = 32;

    public static bool IsValidAccountId(string? value) => IsValidOperationId(value) &&
        value is not ("." or "..") && !value!.Contains('/') && !value.Contains('\\');

    public static bool IsValidRequest(GameWalletBatchRequest? request) =>
        request is { OutcomeSha256.Length: 64, Effects.Count: > 0 and <= MaximumAccounts } &&
        IsValidOperationId(request.OperationId) && request.OutcomeSha256.All(char.IsAsciiHexDigit) &&
        request.Effects.All(effect => effect is { WagerCents: >= 0, PayoutCents: >= 0 } &&
            IsValidAccountId(effect.AccountId) && (effect.WagerCents == 0 || effect.PayoutCents == 0) &&
            GameWalletBalanceRequirement.IsValid(GameWalletSettlementPhase.Direct, effect.MinimumBalanceBeforeCents)) &&
        request.Effects.Select(effect => effect.AccountId).Distinct(StringComparer.Ordinal).Count() == request.Effects.Count;

    public static bool MatchesSettlement(GameWalletBatchReceipt receipt, string gameId, string actorAccountId,
        GameWalletBatchRequest request)
    {
        if (!IsValidRequest(request) || !IsValidAccountId(actorAccountId) ||
            receipt.BatchId != GameWalletBatchIdentity.CreateId(gameId, request.OperationId) ||
            receipt.ActorAccountId != actorAccountId || receipt.SettledAtUtc == default ||
            receipt.SettledAtUtc.Offset != TimeSpan.Zero || receipt.Accounts?.Count != request.Effects.Count)
            return false;

        var anyShort = false;
        for (var index = 0; index < request.Effects.Count; index++)
        {
            var effect = request.Effects[index];
            var entry = receipt.Accounts[index];
            var prefix = GameWalletBatchIdentity.AccountLedgerPrefix(receipt.BatchId, effect.AccountId);
            if (entry is null || entry.AccountId != effect.AccountId || entry.BalanceBeforeCents < 0 || entry.BalanceAfterCents < 0 ||
                entry.InsufficientFunds != (entry.BalanceBeforeCents <
                    GameWalletBalanceRequirement.RequiredCents(effect.WagerCents, effect.MinimumBalanceBeforeCents)))
                return false;

            anyShort |= entry.InsufficientFunds;
            if (receipt.BatchDeclined)
            {
                if (entry.BalanceAfterCents != entry.BalanceBeforeCents ||
                    entry.WagerTransactionId is not null || entry.PayoutTransactionId is not null)
                    return false;
            }
            else if (entry.BalanceAfterCents - entry.BalanceBeforeCents != effect.PayoutCents - effect.WagerCents ||
                entry.WagerTransactionId != (effect.WagerCents > 0 ? $"{prefix}-wager" : null) ||
                entry.PayoutTransactionId != (effect.PayoutCents > 0 ? $"{prefix}-payout" : null))
                return false;
        }
        return receipt.BatchDeclined == anyShort;
    }

    private static bool IsValidOperationId(string? value) => value is { Length: > 0 and <= 256 } &&
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
}
