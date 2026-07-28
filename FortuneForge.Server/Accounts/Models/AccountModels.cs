using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Accounts.Models;

public sealed record CreateAccountRequest(string PlayerName, string Email, string Password);

public sealed record CreateAccountResponse(
    AccountSummary Account,
    bool EmailVerificationRequired,
    bool VerificationEmailSent);

public sealed record ResendVerificationRequest(string Email, string Password);

public sealed record ResendVerificationResponse(
    bool EmailVerified,
    bool VerificationEmailSent);

public sealed record LoginRequest(string Email, string Password, bool RemainLoggedIn);

public sealed record UpdateAccountRequest(string PlayerName);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record DeleteAccountRequest(string Password);

public sealed record AccountBalances(
    long SlotsCredits,
    long FreeGames);

public sealed record SlotStatistics(
    long SpinsPlayed,
    long Wins,
    long Losses,
    long CreditsWagered,
    long CreditsWon,
    long NetCredits);

public sealed record SlotSpinHistoryItem(
    string SpinId,
    string GameId,
    long WageredSlotsCredits,
    long WonSlotsCredits,
    long NetSlotsCredits,
    string Result,
    DateTime CreatedAtUtc);

public sealed record SlotHistoryResponse(IReadOnlyList<SlotSpinHistoryItem> Spins);

public sealed record SlotSpinAdmission(
    long WagerPoints,
    long ChargedWagerPoints,
    bool IsFreeSpin,
    int FreeSpinsRemaining,
    bool SpecialBoostApplied,
    int SpecialPointsRemaining,
    TimeSpan? CooldownRemaining,
    long EnergyBalance,
    string? FreeSpinFeatureMode);

public sealed record SlotSpinSettlement(
    long SlotsCreditsBalance,
    int FreeSpinsRemaining,
    int SpecialPointsBalance,
    long EnergyBalance,
    SpinPayout Payout,
    bool EnergyMultiplierApplied,
    decimal PayoutMultiplier,
    long? FreeSpinWagerPoints,
    IReadOnlyList<SlotSealCollection> SealCollections,
    string? FreeSpinFeatureMode);

public sealed record SlotStateResponse(
    int FreeSpinsRemaining,
    long? FreeSpinWagerPoints,
    int SpecialPointsBalance,
    long EnergyBalance,
    IReadOnlyList<SlotSealCollection> SealCollections,
    string? FreeSpinFeatureMode);

public sealed record AccountSummary(
    string UserId,
    string PlayerName,
    string Email,
    DateTime CreatedAtUtc,
    AccountBalances Balances,
    SlotStatistics Slots,
    string Role);

public sealed record AccountAccessContext(string UserId, string Role)
{
    public bool IsAdmin => string.Equals(Role, "admin", StringComparison.OrdinalIgnoreCase);
}

public sealed record AuthenticationResponse(
    AccountSummary Account,
    string Token,
    DateTime ExpiresAtUtc);

public sealed record StoredAccount(
    AccountSummary Account,
    string NormalizedPlayerName,
    string PasswordHash,
    string Status,
    bool Deactivated);

public enum AccountError
{
    None,
    InvalidPlayerName,
    InvalidEmail,
    InvalidPassword,
    InvalidCredentials,
    Deactivated,
    EmailNotVerified,
    VerificationRateLimited,
    VerificationServiceUnavailable,
    PlayerNameTaken,
    EmailTaken,
    Unauthorized,
    AccountNotFound
}

public sealed class InsufficientSlotCreditsException(long available, long required) : Exception(
    $"This account has {available} slot credits, but the wager requires {required}.")
{
    public long Available { get; } = available;
    public long Required { get; } = required;
}

public sealed class NoFreeSpinsException() : Exception(
    "This account does not have a free game available.");

public sealed class InsufficientSpecialPointsException(long available, long required) : Exception(
    $"This account has {available} special points, but the power boost requires {required}.")
{
    public long Available { get; } = available;
    public long Required { get; } = required;
}

public sealed record AccountResult<T>(T? Value, AccountError Error) where T : class
{
    public static AccountResult<T> Success(T value) => new(value, AccountError.None);

    public static AccountResult<T> Failure(AccountError error) => new(null, error);
}
