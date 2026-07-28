using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    public async Task<AccountResult<StoredAccount>> UpdatePlayerNameAsync(
        string userId,
        string playerName,
        string normalizedPlayerName,
        CancellationToken cancellationToken)
    {
        if (await EnsureAccountSchemaAsync(userId, cancellationToken) is null)
        {
            return AccountResult<StoredAccount>.Failure(AccountError.AccountNotFound);
        }

        var userReference = UserDocument(userId);

        return await database.RunTransactionAsync(
            async transaction =>
            {
                var userSnapshot = await transaction.GetSnapshotAsync(userReference, cancellationToken);
                var slotsCreditsSnapshot = await transaction.GetSnapshotAsync(
                    BalanceDocument(userId, SlotsCreditsCurrencyId),
                    cancellationToken);
                var freeGamesSnapshot = await transaction.GetSnapshotAsync(
                    BalanceDocument(userId, FreeGamesCurrencyId),
                    cancellationToken);
                var statisticsSnapshot = await transaction.GetSnapshotAsync(
                    StatisticsDocument(userId),
                    cancellationToken);
                var currentAccount = ToStoredAccount(
                    userSnapshot,
                    slotsCreditsSnapshot,
                    freeGamesSnapshot,
                    statisticsSnapshot);
                if (currentAccount is null)
                {
                    return AccountResult<StoredAccount>.Failure(AccountError.AccountNotFound);
                }

                if (currentAccount.NormalizedPlayerName == normalizedPlayerName)
                {
                    return AccountResult<StoredAccount>.Success(currentAccount);
                }

                var newKeyReference = PlayerNameKeyDocument(normalizedPlayerName);
                var newKeySnapshot = await transaction.GetSnapshotAsync(
                    newKeyReference,
                    cancellationToken);
                if (newKeySnapshot.Exists)
                {
                    return AccountResult<StoredAccount>.Failure(AccountError.PlayerNameTaken);
                }

                var updatedAtUtc = DateTime.UtcNow;
                var oldKeyReference = PlayerNameKeyDocument(currentAccount.NormalizedPlayerName);
                transaction.Delete(oldKeyReference);
                transaction.Create(newKeyReference, KeyData(userId, updatedAtUtc));
                transaction.Update(userReference, new Dictionary<string, object>
                {
                    ["playerName"] = playerName,
                    ["normalizedPlayerName"] = normalizedPlayerName,
                    ["updatedAt"] = Timestamp.FromDateTime(updatedAtUtc)
                });

                return AccountResult<StoredAccount>.Success(currentAccount with
                {
                    Account = currentAccount.Account with { PlayerName = playerName },
                    NormalizedPlayerName = normalizedPlayerName
                });
            },
            cancellationToken: cancellationToken);
    }

    public async Task<AccountResult<StoredAccount>> ActivateEmailVerifiedAsync(
        string userId,
        DateTime verifiedAtUtc,
        CancellationToken cancellationToken)
    {
        if (await EnsureAccountSchemaAsync(userId, cancellationToken) is null)
        {
            return AccountResult<StoredAccount>.Failure(AccountError.AccountNotFound);
        }

        var userReference = UserDocument(userId);

        return await database.RunTransactionAsync(
            async transaction =>
            {
                var snapshots = await Task.WhenAll(
                    transaction.GetSnapshotAsync(userReference, cancellationToken),
                    transaction.GetSnapshotAsync(
                        BalanceDocument(userId, SlotsCreditsCurrencyId),
                        cancellationToken),
                    transaction.GetSnapshotAsync(
                        BalanceDocument(userId, FreeGamesCurrencyId),
                        cancellationToken),
                    transaction.GetSnapshotAsync(
                        StatisticsDocument(userId),
                        cancellationToken));
                var currentAccount = ToStoredAccount(
                    snapshots[0],
                    snapshots[1],
                    snapshots[2],
                    snapshots[3]);
                if (currentAccount is null)
                {
                    return AccountResult<StoredAccount>.Failure(AccountError.AccountNotFound);
                }

                if (currentAccount.Status == "active")
                {
                    return AccountResult<StoredAccount>.Success(currentAccount);
                }

                transaction.Update(userReference, new Dictionary<string, object>
                {
                    ["status"] = "active",
                    ["emailVerified"] = true,
                    ["emailVerifiedAt"] = Timestamp.FromDateTime(verifiedAtUtc),
                    ["updatedAt"] = Timestamp.FromDateTime(verifiedAtUtc)
                });

                return AccountResult<StoredAccount>.Success(
                    currentAccount with { Status = "active" });
            },
            cancellationToken: cancellationToken);
    }

    public async Task<bool> UpdatePasswordHashAsync(
        string userId,
        string passwordHash,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken)
    {
        var reference = UserDocument(userId);
        var snapshot = await reference.GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists)
        {
            return false;
        }

        await reference.UpdateAsync(
            new Dictionary<string, object>
            {
                ["passwordHash"] = passwordHash,
                ["updatedAt"] = Timestamp.FromDateTime(updatedAtUtc)
            },
            cancellationToken: cancellationToken);
        return true;
    }

    public async Task<bool> DeactivateAsync(string userId, CancellationToken cancellationToken)
    {
        var userReference = UserDocument(userId);
        var deactivatedAtUtc = DateTime.UtcNow;
        var wasDeactivated = await database.RunTransactionAsync(
            async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(userReference, cancellationToken);
                if (!snapshot.Exists)
                {
                    return false;
                }

                transaction.Update(userReference, new Dictionary<string, object>
                {
                    ["deactivated"] = true,
                    ["deactivatedAt"] = Timestamp.FromDateTime(deactivatedAtUtc),
                    ["updatedAt"] = Timestamp.FromDateTime(deactivatedAtUtc)
                });
                return true;
            },
            cancellationToken: cancellationToken);

        if (!wasDeactivated)
        {
            return false;
        }

        await RevokeSessionsAsync(
            database.Collection("accountSessions").WhereEqualTo("userId", userId),
            deactivatedAtUtc,
            cancellationToken);

        return true;
    }
}
