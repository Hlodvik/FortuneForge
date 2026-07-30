using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Accounts.Storage;

public interface IAccountStore
{
    Task<AccountResult<StoredAccount>> CreateAsync(
        string userId,
        string playerName,
        string normalizedPlayerName,
        string email,
        string passwordHash,
        string status,
        CancellationToken cancellationToken);

    Task<StoredAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task<StoredAccount?> FindByIdAsync(string userId, CancellationToken cancellationToken);

    Task CreateSessionAsync(
        string tokenHash,
        string userId,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        string? ipAddress,
        CancellationToken cancellationToken);

    Task<string?> ResolveSessionAsync(
        string tokenHash,
        DateTime nowUtc,
        string? ipAddress,
        CancellationToken cancellationToken);

    Task RevokeSessionAsync(
        string tokenHash,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken);

    Task<AccountResult<StoredAccount>> UpdatePlayerNameAsync(
        string userId,
        string playerName,
        string normalizedPlayerName,
        CancellationToken cancellationToken);

    Task<AccountResult<StoredAccount>> ActivateEmailVerifiedAsync(
        string userId,
        DateTime verifiedAtUtc,
        CancellationToken cancellationToken);

    Task<bool> UpdatePasswordHashAsync(
        string userId,
        string passwordHash,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken);

    Task<SlotSpinAdmission> BeginSlotSpinAsync(
        string userId,
        string gameId,
        long wagerPoints,
        decimal pointValueInCents,
        bool useFreeSpin,
        bool useSpecialBoost,
        int specialBoostCost,
        DateTime startedAtUtc,
        TimeSpan cooldown,
        CancellationToken cancellationToken);

    Task<SlotSpinSettlement> RecordSlotSpinAsync(
        string userId,
        SpinResult result,
        long chargedWagerCents,
        bool isFreeSpin,
        string? activeFreeSpinFeatureMode,
        DateTime createdAtUtc,
        CancellationToken cancellationToken);

    Task<SlotStateResponse> GetSlotStateAsync(
        string userId,
        string gameId,
        decimal pointValueInCents,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SlotSpinHistoryItem>> GetSlotSpinHistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken);

    Task InitializeSchemaAsync(CancellationToken cancellationToken);

    Task<bool> DeactivateAsync(string userId, CancellationToken cancellationToken);
}
