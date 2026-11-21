using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace FortuneForge.Games.Abstractions.Wallet;

[JsonConverter(typeof(JsonStringEnumConverter<GameWalletSettlementPhase>))]
public enum GameWalletSettlementPhase
{
    Direct,
    Reserve,
    CompleteReservation,
}

public sealed record GameWalletSettlementRequest(
    string OperationId,
    string OutcomeSha256,
    long WagerCents,
    long PayoutCents,
    bool SpendLegacyFreeGame = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ExpectedLegacyFreeGamesRemaining = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] GameWalletSettlementPhase Phase = GameWalletSettlementPhase.Direct,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReservationSha256 = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? MinimumBalanceBeforeCents = null);

public static class GameWalletBalanceRequirement
{
    // Completing an accepted reservation must never depend on the account's later balance.
    public static bool IsValid(GameWalletSettlementPhase phase, long? minimumBalanceBeforeCents) =>
        minimumBalanceBeforeCents is null || minimumBalanceBeforeCents >= 0 &&
        phase is GameWalletSettlementPhase.Direct or GameWalletSettlementPhase.Reserve;

    public static long RequiredCents(long wagerCents, long? minimumBalanceBeforeCents) =>
        Math.Max(wagerCents, minimumBalanceBeforeCents ?? 0);
}

public sealed record GameWalletSettlementReceipt(
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool LegacyFreeGamesCountMismatch = false);

public interface IGameWalletClient
{
    Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request,
        CancellationToken cancellationToken);
}

public sealed record GameWalletAccountState(long LegacyFreeGamesRemaining,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? SlotsCreditsBalanceCents = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PlayerName = null)
{
    public static bool IsValidPlayerName(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.AsSpan().SequenceEqual(value.AsSpan().Trim()) && !value.Any(char.IsControl);
}

public interface IGameWalletAccountReader
{
    Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken);
}

public static class GameWalletSettlementIdentity
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string CreateId(string gameId, string accountId, string operationId) =>
        HashId($"{gameId}\n{accountId}\n{operationId}");

    public static string CreateCompletionId(string gameId, string accountId, string operationId) =>
        HashId($"{gameId}\n{accountId}\n{operationId}\ncomplete-reservation");

    private static string HashId(string key)
    {
        return $"wallet-round-{Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(key)))}";
    }
}

public sealed class GameWalletUnavailableException(Exception? inner = null)
    : Exception("The game wallet could not confirm the settlement. Retry the original operation.", inner);

public sealed class GameWalletConflictException()
    : InvalidOperationException("The game wallet operation conflicts with its original settlement.");
