using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    public Task<AccountResult<StoredAccount>> CreateAsync(
        string userId,
        string playerName,
        string normalizedPlayerName,
        string email,
        string passwordHash,
        string status,
        CancellationToken cancellationToken)
    {
        var createdAtUtc = DateTime.UtcNow;
        var userReference = UserDocument(userId);
        var emailKeyReference = EmailKeyDocument(email);
        var playerNameKeyReference = PlayerNameKeyDocument(normalizedPlayerName);
        var slotsCreditsReference = BalanceDocument(userId, SlotsCreditsCurrencyId);
        var freeGamesReference = BalanceDocument(userId, FreeGamesCurrencyId);
        var specialPointsReference = BalanceDocument(userId, SpecialPointsCurrencyId);
        var energyReference = BalanceDocument(userId, EnergyCurrencyId);
        var scopedWukongFreeGamesCurrencyId = GameCurrencyId(FreeGamesCurrencyId, LegacyWukongGameId);
        var scopedWukongSpecialPointsCurrencyId = GameCurrencyId(SpecialPointsCurrencyId, LegacyWukongGameId);
        var scopedWukongEnergyCurrencyId = GameCurrencyId(EnergyCurrencyId, LegacyWukongGameId);
        var scopedWukongFreeGamesReference = BalanceDocument(userId, scopedWukongFreeGamesCurrencyId);
        var scopedWukongSpecialPointsReference = BalanceDocument(userId, scopedWukongSpecialPointsCurrencyId);
        var scopedWukongEnergyReference = BalanceDocument(userId, scopedWukongEnergyCurrencyId);
        var statisticsReference = StatisticsDocument(userId);
        return database.RunTransactionAsync(
            async transaction =>
            {
                var emailSnapshot = await transaction.GetSnapshotAsync(
                    emailKeyReference,
                    cancellationToken);
                if (emailSnapshot.Exists)
                {
                    return AccountResult<StoredAccount>.Failure(AccountError.EmailTaken);
                }

                var playerNameSnapshot = await transaction.GetSnapshotAsync(
                    playerNameKeyReference,
                    cancellationToken);
                if (playerNameSnapshot.Exists)
                {
                    return AccountResult<StoredAccount>.Failure(AccountError.PlayerNameTaken);
                }

                var account = new AccountSummary(
                    userId,
                    playerName,
                    email,
                    createdAtUtc,
                    new AccountBalances(NewAccountSlotsCredits, 0),
                    EmptySlotStatistics(),
                    "player");
                var storedAccount = new StoredAccount(
                    account,
                    normalizedPlayerName,
                    passwordHash,
                    status,
                    false);

                transaction.Create(userReference, UserData(storedAccount, createdAtUtc));
                transaction.Create(emailKeyReference, KeyData(userId, createdAtUtc));
                transaction.Create(playerNameKeyReference, KeyData(userId, createdAtUtc));
                transaction.Create(
                    slotsCreditsReference,
                    BalanceData(userId, SlotsCreditsCurrencyId, NewAccountSlotsCredits, createdAtUtc));
                transaction.Create(
                    freeGamesReference,
                    BalanceData(userId, FreeGamesCurrencyId, 0, createdAtUtc));
                transaction.Create(
                    specialPointsReference,
                    BalanceData(userId, SpecialPointsCurrencyId, 0, createdAtUtc));
                transaction.Create(
                    energyReference,
                    BalanceData(userId, EnergyCurrencyId, 0, createdAtUtc));
                transaction.Create(
                    scopedWukongFreeGamesReference,
                    BalanceData(userId, scopedWukongFreeGamesCurrencyId, 0, createdAtUtc));
                transaction.Create(
                    scopedWukongSpecialPointsReference,
                    BalanceData(userId, scopedWukongSpecialPointsCurrencyId, 0, createdAtUtc));
                transaction.Create(
                    scopedWukongEnergyReference,
                    BalanceData(userId, scopedWukongEnergyCurrencyId, 0, createdAtUtc));
                transaction.Create(statisticsReference, StatisticsData(userId, createdAtUtc));
                transaction.Set(
                    CurrencyDocument(SlotsCreditsCurrencyId),
                    CurrencyData(SlotsCreditsCurrencyId, "Slots credits", 0, createdAtUtc),
                    SetOptions.MergeAll);
                transaction.Set(
                    CurrencyDocument(FreeGamesCurrencyId),
                    CurrencyData(FreeGamesCurrencyId, "Free games", 0, createdAtUtc),
                    SetOptions.MergeAll);
                transaction.Set(
                    CurrencyDocument(SpecialPointsCurrencyId),
                    CurrencyData(SpecialPointsCurrencyId, "Wukong power points", 0, createdAtUtc),
                    SetOptions.MergeAll);
                transaction.Set(
                    CurrencyDocument(EnergyCurrencyId),
                    CurrencyData(EnergyCurrencyId, "Energy", 0, createdAtUtc),
                    SetOptions.MergeAll);

                return AccountResult<StoredAccount>.Success(storedAccount);
            },
            cancellationToken: cancellationToken);
    }

    public async Task<StoredAccount?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var keySnapshot = await EmailKeyDocument(email).GetSnapshotAsync(cancellationToken);
        if (!keySnapshot.Exists)
        {
            return null;
        }

        return await FindByIdAsync(keySnapshot.GetValue<string>("userId"), cancellationToken);
    }

    public async Task<StoredAccount?> FindByIdAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var account = await ReadStoredAccountAsync(userId, cancellationToken);
        return account is not null
            ? account
            : await EnsureAccountSchemaAsync(userId, cancellationToken);
    }
}
