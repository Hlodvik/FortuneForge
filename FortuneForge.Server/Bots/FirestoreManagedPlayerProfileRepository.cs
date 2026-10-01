using System.Security.Cryptography;
using System.Text;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Bots;

internal sealed class FirestoreManagedPlayerProfileRepository(
    FirestoreDb database) : IManagedPlayerProfileRepository
{
    private static readonly string[] CurrencyIds =
        ["slotsCredits", "freeGames", "specialPoints", "energy"];

    public async Task<bool> TryCreateAsync(
        ManagedPlayerProfile profile,
        CancellationToken cancellationToken)
    {
        var legacyProfiles = await database.Collection("users")
            .WhereArrayContains(
                ManagedPlayerProfileSchema.ProfileTagsField,
                ManagedPlayerProfileSchema.BotTag)
            .GetSnapshotAsync(cancellationToken);
        var legacyNames = legacyProfiles.Documents
            .Where(IsManagedPlayer)
            .Select(snapshot => ReadString(snapshot, "playerName"))
            .Where(name => name.Length > 0)
            .ToArray();

        return await database.RunTransactionAsync(async transaction =>
        {
            var userReference = User(profile.UserId);
            var nameReference = PlayerNameKey(profile.PlayerName);
            var registryReference = ManagedPlayerNameRegistry();
            var snapshots = await Task.WhenAll(
                transaction.GetSnapshotAsync(userReference, cancellationToken),
                transaction.GetSnapshotAsync(nameReference, cancellationToken),
                transaction.GetSnapshotAsync(registryReference, cancellationToken));
            if (snapshots[0].Exists || snapshots[1].Exists) return false;

            var registeredNames = RegistryNames(snapshots[2]);
            var knownNames = legacyNames.Concat(registeredNames).ToArray();
            if (!ManagedPlayerNamePolicy.IsDistinct(profile.PlayerName, knownNames)) return false;

            transaction.Create(userReference, UserData(profile));
            transaction.Create(nameReference, KeyData(profile.UserId, profile.CreatedAtUtc));
            transaction.Set(registryReference, NameRegistryData(
                knownNames.Append(profile.PlayerName), profile.CreatedAtUtc), SetOptions.MergeAll);
            EnsureSupportingProfileDocuments(transaction, profile);
            return true;
        }, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ManagedPlayerProfile>> ListSupportingAsync(
        string gameId,
        int limit,
        CancellationToken cancellationToken)
    {
        var snapshot = await database.Collection("users")
            .WhereArrayContains(
                ManagedPlayerProfileSchema.ProfileTagsField,
                ManagedPlayerProfileSchema.BotTag)
            .Limit(limit)
            .GetSnapshotAsync(cancellationToken);
        return snapshot.Documents
            .Where(IsManagedPlayer)
            .Select(ReadProfile)
            .Where(profile => profile.SupportedGames.Contains(gameId))
            .ToArray();
    }

    public Task MarkLastActiveAsync(
        string profileId,
        DateTime nowUtc,
        CancellationToken cancellationToken) => database.RunTransactionAsync(async transaction =>
        {
            var reference = User(profileId);
            var profile = await transaction.GetSnapshotAsync(reference, cancellationToken);
            if (!IsManagedPlayer(profile)) return false;
            transaction.Set(reference, new Dictionary<string, object>
            {
                ["lastActiveAt"] = Timestamp.FromDateTime(nowUtc),
                ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
            }, SetOptions.MergeAll);
            return true;
        }, cancellationToken: cancellationToken);

    private void EnsureSupportingProfileDocuments(
        Transaction transaction,
        ManagedPlayerProfile profile)
    {
        transaction.Set(
            Statistics(profile.UserId),
            StatisticsData(profile.UserId, profile.CreatedAtUtc),
            SetOptions.MergeAll);
        foreach (var currencyId in CurrencyIds)
        {
            transaction.Set(
                Balance(profile.UserId, currencyId),
                BalanceData(profile.UserId, currencyId, profile.CreatedAtUtc),
                SetOptions.MergeAll);
        }
    }

    private static ManagedPlayerProfile ReadProfile(DocumentSnapshot snapshot)
    {
        var games = snapshot.TryGetValue<List<object>>("supportedGames", out var values)
            ? values.Select(value => value.ToString() ?? string.Empty).Where(value => value.Length > 0)
            : [];
        return new(
            snapshot.Id,
            ReadString(snapshot, "playerName"),
            checked((int)ReadLong(snapshot, "skillLevel", 3)),
            games.ToHashSet(StringComparer.OrdinalIgnoreCase),
            ReadTimestamp(snapshot, "createdAt"));
    }

    private static Dictionary<string, object> UserData(ManagedPlayerProfile profile) => new()
    {
        ["userId"] = profile.UserId,
        ["playerName"] = profile.PlayerName,
        ["normalizedPlayerName"] = profile.PlayerName.Trim().ToUpperInvariant(),
        ["email"] = $"{profile.UserId}@managed.fortuneforge.invalid",
        ["passwordHash"] = string.Empty,
        ["status"] = "active",
        ["deactivated"] = false,
        ["authProvider"] = ManagedPlayerProfileSchema.AuthenticationProvider,
        [ManagedPlayerProfileSchema.ProfileTagsField] = new[] { ManagedPlayerProfileSchema.BotTag },
        ["firebaseUid"] = string.Empty,
        ["emailVerified"] = true,
        ["role"] = "player",
        ["skillLevel"] = profile.SkillLevel,
        ["supportedGames"] = profile.SupportedGames.ToArray(),
        ["managedPlayWindowStartedAt"] = Timestamp.FromDateTime(profile.CreatedAtUtc),
        ["managedPlaySecondsInWindow"] = 0L,
        ["managedNextAvailableAt"] = Timestamp.FromDateTime(profile.CreatedAtUtc),
        ["accountSchemaVersion"] = 7L,
        ["createdAt"] = Timestamp.FromDateTime(profile.CreatedAtUtc),
        ["updatedAt"] = Timestamp.FromDateTime(profile.CreatedAtUtc)
    };

    private static Dictionary<string, object> StatisticsData(string userId, DateTime createdAtUtc) => new()
    {
        ["userId"] = userId,
        ["spinsPlayed"] = 0L,
        ["wins"] = 0L,
        ["losses"] = 0L,
        ["creditsWagered"] = 0L,
        ["creditsWon"] = 0L,
        ["netCredits"] = 0L,
        ["createdAt"] = Timestamp.FromDateTime(createdAtUtc),
        ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
    };

    private static Dictionary<string, object> BalanceData(
        string userId,
        string currencyId,
        DateTime createdAtUtc) => new()
    {
        ["userId"] = userId,
        ["currencyId"] = currencyId,
        ["available"] = 0L,
        ["reserved"] = 0L,
        ["version"] = 1L,
        ["createdAt"] = Timestamp.FromDateTime(createdAtUtc),
        ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
    };

    private static Dictionary<string, object> KeyData(string userId, DateTime nowUtc) => new()
    {
        ["userId"] = userId,
        ["createdAt"] = Timestamp.FromDateTime(nowUtc)
    };

    private DocumentReference User(string userId) => database.Collection("users").Document(userId);
    private DocumentReference Statistics(string userId) =>
        database.Collection("userSlotStatistics").Document(userId);
    private DocumentReference ManagedPlayerNameRegistry() =>
        database.Collection("managedPlayerInfrastructure").Document("nameRegistry");
    private DocumentReference PlayerNameKey(string playerName) =>
        database.Collection("accountPlayerNameKeys")
            .Document(Hash(playerName.Trim().ToUpperInvariant()));
    private DocumentReference Balance(string userId, string currencyId) =>
        database.Collection("userBalances").Document($"{userId}_{currencyId}");

    private static string Hash(string value) => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string ReadString(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<string>(field, out var value) ? value : string.Empty;
    internal static bool IsManagedPlayer(DocumentSnapshot snapshot) =>
        snapshot.Exists &&
        ReadString(snapshot, "authProvider") == ManagedPlayerProfileSchema.AuthenticationProvider &&
        snapshot.TryGetValue<List<object>>(
            ManagedPlayerProfileSchema.ProfileTagsField, out var tags) &&
        tags.Any(value => string.Equals(
            value?.ToString(), ManagedPlayerProfileSchema.BotTag, StringComparison.Ordinal));
    private static long ReadLong(DocumentSnapshot snapshot, string field, long fallback = 0) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : fallback;
    private static DateTime ReadTimestamp(DocumentSnapshot snapshot, string field) =>
        snapshot.Exists && snapshot.TryGetValue<Timestamp>(field, out var value)
            ? value.ToDateTime()
            : DateTime.UnixEpoch;
    private static IReadOnlyList<string> RegistryNames(DocumentSnapshot snapshot) =>
        snapshot.Exists && snapshot.TryGetValue<List<object>>("normalizedNames", out var values)
            ? values.Select(value => value?.ToString() ?? string.Empty)
                .Where(value => value.Length > 0).ToArray()
            : [];
    private static Dictionary<string, object> NameRegistryData(
        IEnumerable<string> names,
        DateTime nowUtc) => new()
    {
        ["normalizedNames"] = names.Select(ManagedPlayerNamePolicy.Normalize)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray(),
        ["updatedAt"] = Timestamp.FromDateTime(nowUtc)
    };
}
