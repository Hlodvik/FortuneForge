using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed partial class FirestoreAccountStore
{
    private async Task<StoredAccount?> ReadStoredAccountAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var snapshots = await Task.WhenAll(
            UserDocument(userId).GetSnapshotAsync(cancellationToken),
            BalanceDocument(userId, SlotsCreditsCurrencyId).GetSnapshotAsync(cancellationToken),
            BalanceDocument(userId, FreeGamesCurrencyId).GetSnapshotAsync(cancellationToken),
            BalanceDocument(userId, SpecialPointsCurrencyId).GetSnapshotAsync(cancellationToken),
            BalanceDocument(userId, EnergyCurrencyId).GetSnapshotAsync(cancellationToken),
            StatisticsDocument(userId).GetSnapshotAsync(cancellationToken));

        if (!snapshots[3].Exists || !snapshots[4].Exists)
        {
            return null;
        }

        return ToStoredAccount(
            snapshots[0],
            snapshots[1],
            snapshots[2],
            snapshots[5]);
    }

    private static StoredAccount? ToStoredAccount(
        DocumentSnapshot userSnapshot,
        DocumentSnapshot slotsCreditsSnapshot,
        DocumentSnapshot freeGamesSnapshot,
        DocumentSnapshot statisticsSnapshot)
    {
        if (!userSnapshot.Exists ||
            !slotsCreditsSnapshot.Exists ||
            !freeGamesSnapshot.Exists ||
            !statisticsSnapshot.Exists)
        {
            return null;
        }

        return ToStoredAccount(
            userSnapshot,
            ReadRandBalance(slotsCreditsSnapshot),
            ReadLong(freeGamesSnapshot, "available"),
            ToSlotStatistics(statisticsSnapshot));
    }

    private static StoredAccount? ToStoredAccount(
        DocumentSnapshot userSnapshot,
        decimal slotsCredits,
        long freeGames,
        SlotStatistics statistics)
    {
        if (!userSnapshot.Exists)
        {
            return null;
        }

        var account = new AccountSummary(
            userSnapshot.GetValue<string>("userId"),
            userSnapshot.GetValue<string>("playerName"),
            userSnapshot.GetValue<string>("email"),
            userSnapshot.GetValue<Timestamp>("createdAt").ToDateTime(),
            new AccountBalances(slotsCredits, freeGames),
            statistics,
            userSnapshot.TryGetValue<string>("role", out var role) ? role : "player");

        return new StoredAccount(
            account,
            userSnapshot.GetValue<string>("normalizedPlayerName"),
            userSnapshot.GetValue<string>("passwordHash"),
            userSnapshot.GetValue<string>("status"),
            userSnapshot.TryGetValue<bool>("deactivated", out var deactivated) && deactivated);
    }

    private static StoredAccount? ToStoredAccount(DocumentSnapshot userSnapshot) =>
        ToStoredAccount(
            userSnapshot,
            ReadLong(userSnapshot, "balancePoints", LegacySlotsCreditsFallback),
            0,
            EmptySlotStatistics());

    private static long ReadLong(
        DocumentSnapshot snapshot,
        string field,
        long fallback = 0) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : fallback;

    private static decimal ReadDecimal(
        DocumentSnapshot snapshot,
        string field,
        decimal fallback = 0)
    {
        if (!snapshot.Exists)
        {
            return fallback;
        }
        if (snapshot.TryGetValue<long>(field, out var longValue))
        {
            return longValue;
        }
        return snapshot.TryGetValue<double>(field, out var doubleValue)
            ? (decimal)doubleValue
            : fallback;
    }

    private static long ReadRandBalanceCents(DocumentSnapshot snapshot) =>
        RandMoney.CombineCents(
            ReadLong(snapshot, "available"),
            ReadLong(snapshot, AvailableFractionalCentsField));

    private static decimal ReadRandBalance(DocumentSnapshot snapshot) =>
        RandMoney.CentsToRand(ReadRandBalanceCents(snapshot));

    private static string? ReadString(DocumentSnapshot snapshot, string field)
    {
        if (!snapshot.Exists ||
            !snapshot.TryGetValue<string>(field, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }

    private static Dictionary<string, long> ReadLongMap(DocumentSnapshot snapshot, string field)
    {
        var values = SealFeatureModes.ToDictionary(
            mode => mode,
            _ => 0L,
            StringComparer.Ordinal);
        if (!snapshot.Exists)
        {
            return values;
        }

        var data = snapshot.ToDictionary();
        if (!data.TryGetValue(field, out var rawValue) ||
            rawValue is not IDictionary<string, object> rawMap)
        {
            return values;
        }

        foreach (var mode in SealFeatureModes)
        {
            if (rawMap.TryGetValue(mode, out var value))
            {
                values[mode] = CoerceLong(value);
            }
        }

        return values;
    }

    private static long CoerceLong(object? value) => value switch
    {
        null => 0L,
        long longValue => longValue,
        int intValue => intValue,
        double doubleValue => checked((long)doubleValue),
        decimal decimalValue => checked((long)decimalValue),
        _ => long.TryParse(value.ToString(), out var parsed) ? parsed : 0L
    };
}
