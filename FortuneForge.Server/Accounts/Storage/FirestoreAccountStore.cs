using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Accounts.Storage;

public sealed class FirestoreAccountStore(FirestoreDb database) : IAccountStore
{
    private const long NewAccountSlotsCredits = 0;
    private const long LegacySlotsCreditsFallback = 10_000;
    private const long AccountSchemaVersion = 7;
    private const string LegacyLoadedMoneyCurrencyId = "loadedMoney";
    private const string SlotsCreditsCurrencyId = "slotsCredits";
    private const string FreeGamesCurrencyId = "freeGames";
    private const string SpecialPointsCurrencyId = "specialPoints";
    private const string EnergyCurrencyId = "energy";
    private const string LegacyWukongGameId = "classic-demo-v1";
    private const int SealCompletionTarget = 44;
    private const int SealCompletionFreeSpins = 10;
    private const string SyncedReelsFeatureMode = "sync";
    private const string ExtraRowsFeatureMode = "rows";
    private const string PawBoostFeatureMode = "paw";
    private const string RandColumnFeatureMode = "rand";
    private static readonly IReadOnlyDictionary<string, string> SealFeatureModesBySymbolId =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SEAL_SYNC"] = SyncedReelsFeatureMode,
            ["SEAL_ROWS"] = ExtraRowsFeatureMode,
            ["SEAL_PAW"] = PawBoostFeatureMode,
            ["SEAL_RAND"] = RandColumnFeatureMode
        };
    private static readonly string[] SealFeatureModes =
    [
        SyncedReelsFeatureMode,
        ExtraRowsFeatureMode,
        PawBoostFeatureMode,
        RandColumnFeatureMode
    ];

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

    public Task CreateSessionAsync(
        string tokenHash,
        string userId,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var session = new Dictionary<string, object>
        {
            ["userId"] = userId,
            ["createdAt"] = Timestamp.FromDateTime(createdAtUtc),
            ["expiresAt"] = Timestamp.FromDateTime(expiresAtUtc),
            ["lastSeenAt"] = Timestamp.FromDateTime(createdAtUtc),
            ["revoked"] = false
        };
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            session["createdIp"] = ipAddress;
            session["lastSeenIp"] = ipAddress;
        }

        return database.Collection("accountSessions").Document(tokenHash).CreateAsync(
            session,
            cancellationToken);
    }

    public async Task<string?> ResolveSessionAsync(
        string tokenHash,
        DateTime nowUtc,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var reference = database.Collection("accountSessions").Document(tokenHash);
        var snapshot = await reference.GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists ||
            snapshot.GetValue<bool>("revoked") ||
            snapshot.GetValue<Timestamp>("expiresAt").ToDateTime() <= nowUtc)
        {
            return null;
        }

        var shouldRefreshLastSeen = !snapshot.TryGetValue<Timestamp>("lastSeenAt", out var lastSeenAt) ||
            nowUtc - lastSeenAt.ToDateTime() >= TimeSpan.FromMinutes(15);
        var ipChanged = !string.IsNullOrWhiteSpace(ipAddress) &&
            (!snapshot.TryGetValue<string>("lastSeenIp", out var lastSeenIp) ||
                !string.Equals(lastSeenIp, ipAddress, StringComparison.Ordinal));
        if (shouldRefreshLastSeen || ipChanged)
        {
            var updates = new Dictionary<string, object>
            {
                ["lastSeenAt"] = Timestamp.FromDateTime(nowUtc)
            };
            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                updates["lastSeenIp"] = ipAddress;
            }

            await reference.UpdateAsync(updates, cancellationToken: cancellationToken);
        }

        return snapshot.GetValue<string>("userId");
    }

    public async Task RevokeSessionAsync(
        string tokenHash,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken)
    {
        var reference = database.Collection("accountSessions").Document(tokenHash);
        var snapshot = await reference.GetSnapshotAsync(cancellationToken);
        if (!snapshot.Exists)
        {
            return;
        }

        await reference.UpdateAsync(
            new Dictionary<string, object>
            {
                ["revoked"] = true,
                ["revokedAt"] = Timestamp.FromDateTime(revokedAtUtc)
            },
            cancellationToken: cancellationToken);
    }

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

    public Task<SlotSpinSettlement> RecordSlotSpinAsync(
        string userId,
        SpinResult result,
        long chargedWagerPoints,
        bool isFreeSpin,
        string? activeFreeSpinFeatureMode,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        var resultReference = SlotSpinResultDocument(result.SpinId);
        var statisticsReference = StatisticsDocument(userId);
        var slotsCreditsReference = BalanceDocument(userId, SlotsCreditsCurrencyId);
        var freeGamesCurrencyId = GameCurrencyId(FreeGamesCurrencyId, result.GameId);
        var specialPointsCurrencyId = GameCurrencyId(SpecialPointsCurrencyId, result.GameId);
        var energyCurrencyId = GameCurrencyId(EnergyCurrencyId, result.GameId);
        var freeGamesReference = BalanceDocument(userId, freeGamesCurrencyId);
        var specialPointsReference = BalanceDocument(userId, specialPointsCurrencyId);
        var energyReference = BalanceDocument(userId, energyCurrencyId);
        var guardReference = SlotSpinGuardDocument(userId, result.GameId);
        var spinKey = result.SpinId.ToString("N");
        var wagerTransactionReference = BalanceTransactionDocument($"{spinKey}-wager");
        var payoutTransactionReference = BalanceTransactionDocument($"{spinKey}-payout");
        var freeGamesAwardTransactionReference = BalanceTransactionDocument(
            $"{spinKey}-free-games-award");
        var specialPointsAwardTransactionReference = BalanceTransactionDocument(
            $"{spinKey}-special-points-award");
        var energyAwardTransactionReference = BalanceTransactionDocument(
            $"{spinKey}-energy-award");
        var energyResetTransactionReference = BalanceTransactionDocument(
            $"{spinKey}-energy-multiplier-use");

        return database.RunTransactionAsync(
            async transaction =>
            {
                var existingResult = await transaction.GetSnapshotAsync(
                    resultReference,
                    cancellationToken);
                var slotsCreditsSnapshot = await transaction.GetSnapshotAsync(
                    slotsCreditsReference,
                    cancellationToken);
                var freeGamesSnapshot = await transaction.GetSnapshotAsync(
                    freeGamesReference,
                    cancellationToken);
                var specialPointsSnapshot = await transaction.GetSnapshotAsync(
                    specialPointsReference,
                    cancellationToken);
                var energySnapshot = await transaction.GetSnapshotAsync(
                    energyReference,
                    cancellationToken);
                var guardSnapshot = await transaction.GetSnapshotAsync(
                    guardReference,
                    cancellationToken);
                var availableCredits = ReadLong(slotsCreditsSnapshot, "available");
                var currentFreeGames = ReadLong(freeGamesSnapshot, "available");
                var currentSpecialPoints = ReadLong(specialPointsSnapshot, "available");
                var currentEnergy = ReadLong(energySnapshot, "available");
                if (existingResult.Exists)
                {
                    var existingSealCollections = CreateSealCollections(
                        ReadLongMap(guardSnapshot, "sealCounts"),
                        ReadLongMap(guardSnapshot, "sealWagerTotals"));
                    var existingFreeSpinWagerPoints = ReadLong(guardSnapshot, "freeSpinWagerPoints");
                    return new SlotSpinSettlement(
                        availableCredits,
                        checked((int)currentFreeGames),
                        checked((int)currentSpecialPoints),
                        currentEnergy,
                        result.Payout,
                        false,
                        1m,
                        existingFreeSpinWagerPoints > 0 ? existingFreeSpinWagerPoints : null,
                        existingSealCollections,
                        ReadString(guardSnapshot, "freeSpinFeatureMode"));
                }

                if (availableCredits < chargedWagerPoints)
                {
                    throw new InsufficientSlotCreditsException(availableCredits, chargedWagerPoints);
                }

                var energyBonus = EnergyBonus.Settle(currentEnergy, result.EnergyAwarded, result.Payout);
                var sealSettlement = SettleSealCollections(
                    guardSnapshot,
                    result,
                    energyBonus.MultiplierApplied);
                var settledPayout = energyBonus.Payout;
                var netCredits = checked(settledPayout.TotalPoints - chargedWagerPoints);
                var isWin = settledPayout.TotalPoints > 0;
                var balanceAfterWager = checked(availableCredits - chargedWagerPoints);
                var balanceAfterPayout = checked(balanceAfterWager + settledPayout.TotalPoints);
                var totalFreeSpinsAwarded = checked(
                    result.FreeSpinsAwarded + sealSettlement.FreeSpinsAwarded);
                var freeGamesRemaining = checked(
                    (isFreeSpin ? currentFreeGames : 0) + totalFreeSpinsAwarded);
                var specialPointsBalance = checked(currentSpecialPoints + result.SpecialPointsAwarded);
                var energyBalance = energyBonus.FinalEnergyBalance;
                var nextFreeSpinFeatureMode = freeGamesRemaining > 0
                    ? sealSettlement.FreeSpinFeatureMode ?? (isFreeSpin ? activeFreeSpinFeatureMode : null)
                    : null;
                var nextFreeSpinWagerPoints = freeGamesRemaining > 0
                    ? sealSettlement.FreeSpinWagerPoints > 0
                        ? sealSettlement.FreeSpinWagerPoints
                        : result.WagerPoints
                    : 0;

                transaction.Create(resultReference, new Dictionary<string, object>
                {
                    ["spinId"] = result.SpinId.ToString("N"),
                    ["userId"] = userId,
                    ["gameId"] = result.GameId,
                    ["reelSetId"] = result.ReelSetId,
                    ["symbolSetId"] = result.SymbolSetId,
                    ["paytableId"] = result.PaytableId,
                    ["reelStops"] = result.ReelStops.Select(static stop => (long)stop).ToArray(),
                    ["wageredSlotsCredits"] = chargedWagerPoints,
                    ["payoutWagerPoints"] = result.WagerPoints,
                    ["wonSlotsCredits"] = settledPayout.TotalPoints,
                    ["netSlotsCredits"] = netCredits,
                    ["isFreeSpin"] = isFreeSpin,
                    ["freeSpinsAwarded"] = result.FreeSpinsAwarded,
                    ["specialPointsAwarded"] = result.SpecialPointsAwarded,
                    ["energyAwarded"] = result.EnergyAwarded,
                    ["energyMultiplierApplied"] = energyBonus.MultiplierApplied,
                    ["payoutMultiplier"] = (double)energyBonus.PayoutMultiplier,
                    ["monkeyPawCount"] = result.MonkeyPawCount,
                    ["moneyGrabPoints"] = result.MoneyGrabPoints,
                    ["bananaBonusPoints"] = result.BananaBonusPoints,
                    ["sealsAwarded"] = result.SealsAwarded.ToDictionary(
                        pair => pair.Key,
                        pair => (object)(long)pair.Value,
                        StringComparer.Ordinal),
                    ["sealFreeSpinsAwarded"] = sealSettlement.FreeSpinsAwarded,
                    ["sealFreeSpinFeatureMode"] = sealSettlement.FreeSpinFeatureMode ?? string.Empty,
                    ["specialBoostApplied"] = result.SpecialBoostApplied,
                    ["consecutiveFiveMisses"] = result.ConsecutiveFiveMisses,
                    ["fiveMatchPityTriggered"] = result.FiveMatchPityTriggered,
                    ["outcomeSchemaVersion"] = 3,
                    ["result"] = isWin ? "win" : "loss",
                    ["createdAt"] = Timestamp.FromDateTime(createdAtUtc)
                });
                transaction.Update(slotsCreditsReference, new Dictionary<string, object>
                {
                    ["available"] = balanceAfterPayout,
                    ["version"] = FieldValue.Increment(1),
                    ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
                });
                if (chargedWagerPoints > 0)
                {
                    transaction.Create(
                        wagerTransactionReference,
                        BalanceTransactionData(
                            wagerTransactionReference.Id,
                            userId,
                            SlotsCreditsCurrencyId,
                            -chargedWagerPoints,
                            balanceAfterWager,
                            "slot-wager",
                            wagerTransactionReference.Id,
                            createdAtUtc));
                }
                if (settledPayout.TotalPoints > 0)
                {
                    transaction.Create(
                        payoutTransactionReference,
                        BalanceTransactionData(
                            payoutTransactionReference.Id,
                            userId,
                            SlotsCreditsCurrencyId,
                            settledPayout.TotalPoints,
                            balanceAfterPayout,
                            "slot-payout",
                            payoutTransactionReference.Id,
                            createdAtUtc));
                }
                transaction.Set(statisticsReference, new Dictionary<string, object>
                {
                    ["userId"] = userId,
                    ["spinsPlayed"] = FieldValue.Increment(1),
                    ["wins"] = FieldValue.Increment(isWin ? 1 : 0),
                    ["losses"] = FieldValue.Increment(isWin ? 0 : 1),
                    ["creditsWagered"] = FieldValue.Increment(chargedWagerPoints),
                    ["creditsWon"] = FieldValue.Increment(settledPayout.TotalPoints),
                    ["netCredits"] = FieldValue.Increment(netCredits),
                    ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
                }, SetOptions.MergeAll);

                var shouldWriteFreeGamesBalance =
                    totalFreeSpinsAwarded > 0 ||
                    (!isFreeSpin && currentFreeGames != freeGamesRemaining);
                if (shouldWriteFreeGamesBalance)
                {
                    if (freeGamesSnapshot.Exists)
                    {
                        transaction.Update(freeGamesReference, new Dictionary<string, object>
                        {
                            ["available"] = freeGamesRemaining,
                            ["version"] = FieldValue.Increment(1),
                            ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
                        });
                    }
                    else
                    {
                        transaction.Create(
                            freeGamesReference,
                            BalanceData(userId, freeGamesCurrencyId, freeGamesRemaining, createdAtUtc));
                    }
                }

                if (totalFreeSpinsAwarded > 0)
                {
                    transaction.Create(
                        freeGamesAwardTransactionReference,
                        BalanceTransactionData(
                            freeGamesAwardTransactionReference.Id,
                            userId,
                            freeGamesCurrencyId,
                            totalFreeSpinsAwarded,
                            freeGamesRemaining,
                            "free-game-award",
                            freeGamesAwardTransactionReference.Id,
                            createdAtUtc));
                }

                if (totalFreeSpinsAwarded > 0 ||
                    sealSettlement.SealsChanged ||
                    (!isFreeSpin && currentFreeGames > 0) ||
                    (isFreeSpin && currentFreeGames == 0 && !string.IsNullOrWhiteSpace(activeFreeSpinFeatureMode)))
                {
                    transaction.Set(guardReference, new Dictionary<string, object>
                    {
                        ["userId"] = userId,
                        ["freeSpinWagerPoints"] = nextFreeSpinWagerPoints,
                        ["freeSpinFeatureMode"] = nextFreeSpinFeatureMode ?? string.Empty,
                        ["sealCounts"] = sealSettlement.SealCounts.ToDictionary(
                            pair => pair.Key,
                            pair => (object)pair.Value,
                            StringComparer.Ordinal),
                        ["sealWagerTotals"] = sealSettlement.SealWagerTotals.ToDictionary(
                            pair => pair.Key,
                            pair => (object)pair.Value,
                            StringComparer.Ordinal),
                        ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
                    }, SetOptions.MergeAll);
                }

                if (result.SpecialPointsAwarded > 0)
                {
                    if (specialPointsSnapshot.Exists)
                    {
                        transaction.Update(specialPointsReference, new Dictionary<string, object>
                        {
                            ["available"] = specialPointsBalance,
                            ["version"] = FieldValue.Increment(1),
                            ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
                        });
                    }
                    else
                    {
                        transaction.Create(
                            specialPointsReference,
                            BalanceData(
                                userId,
                                specialPointsCurrencyId,
                                specialPointsBalance,
                                createdAtUtc));
                    }
                    transaction.Create(
                        specialPointsAwardTransactionReference,
                        BalanceTransactionData(
                            specialPointsAwardTransactionReference.Id,
                            userId,
                            specialPointsCurrencyId,
                            result.SpecialPointsAwarded,
                            specialPointsBalance,
                            "special-point-award",
                            specialPointsAwardTransactionReference.Id,
                            createdAtUtc));
                }

                var shouldWriteEnergyBalance =
                    result.EnergyAwarded > 0 ||
                    energyBonus.MultiplierApplied ||
                    currentEnergy != energyBonus.FinalEnergyBalance;
                if (shouldWriteEnergyBalance)
                {
                    if (energySnapshot.Exists)
                    {
                        transaction.Update(energyReference, new Dictionary<string, object>
                        {
                            ["available"] = energyBalance,
                            ["version"] = FieldValue.Increment(1),
                            ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
                        });
                    }
                    else
                    {
                        transaction.Create(
                            energyReference,
                            BalanceData(userId, energyCurrencyId, energyBalance, createdAtUtc));
                    }
                }

                if (energyBonus.EnergyAddedToMeter > 0)
                {
                    transaction.Create(
                        energyAwardTransactionReference,
                        BalanceTransactionData(
                            energyAwardTransactionReference.Id,
                            userId,
                            energyCurrencyId,
                            energyBonus.EnergyAddedToMeter,
                            energyBonus.MeterBalanceBeforeReset,
                            "energy-award",
                            energyAwardTransactionReference.Id,
                            createdAtUtc));
                }

                if (energyBonus.MultiplierApplied)
                {
                    transaction.Create(
                        energyResetTransactionReference,
                        BalanceTransactionData(
                            energyResetTransactionReference.Id,
                            userId,
                            energyCurrencyId,
                            -energyBonus.MeterBalanceBeforeReset,
                            energyBalance,
                            "energy-multiplier-use",
                            energyResetTransactionReference.Id,
                            createdAtUtc));
                }

                return new SlotSpinSettlement(
                    balanceAfterPayout,
                    checked((int)freeGamesRemaining),
                    checked((int)specialPointsBalance),
                    energyBalance,
                    settledPayout,
                    energyBonus.MultiplierApplied,
                    energyBonus.PayoutMultiplier,
                    nextFreeSpinWagerPoints > 0 ? nextFreeSpinWagerPoints : null,
                    sealSettlement.Collections,
                    nextFreeSpinFeatureMode);
            },
            cancellationToken: cancellationToken);
    }

    public Task<SlotSpinAdmission> BeginSlotSpinAsync(
        string userId,
        string gameId,
        long wagerPoints,
        bool useFreeSpin,
        bool useSpecialBoost,
        int specialBoostCost,
        DateTime startedAtUtc,
        TimeSpan cooldown,
        CancellationToken cancellationToken)
    {
        var guardReference = SlotSpinGuardDocument(userId, gameId);
        var slotsCreditsReference = BalanceDocument(userId, SlotsCreditsCurrencyId);
        var freeGamesCurrencyId = GameCurrencyId(FreeGamesCurrencyId, gameId);
        var specialPointsCurrencyId = GameCurrencyId(SpecialPointsCurrencyId, gameId);
        var energyCurrencyId = GameCurrencyId(EnergyCurrencyId, gameId);
        var freeGamesReference = BalanceDocument(userId, freeGamesCurrencyId);
        var specialPointsReference = BalanceDocument(userId, specialPointsCurrencyId);
        var energyReference = BalanceDocument(userId, energyCurrencyId);
        var freeGameUseTransactionReference = BalanceTransactionDocument(
            $"{Guid.NewGuid():N}-free-game-use");
        var specialPointUseTransactionReference = BalanceTransactionDocument(
            $"{Guid.NewGuid():N}-special-boost-use");

        return database.RunTransactionAsync(
            async transaction =>
            {
                var snapshots = await Task.WhenAll(
                    transaction.GetSnapshotAsync(guardReference, cancellationToken),
                    transaction.GetSnapshotAsync(slotsCreditsReference, cancellationToken),
                    transaction.GetSnapshotAsync(freeGamesReference, cancellationToken),
                    transaction.GetSnapshotAsync(specialPointsReference, cancellationToken),
                    transaction.GetSnapshotAsync(energyReference, cancellationToken));
                var guardSnapshot = snapshots[0];
                var slotsCreditsSnapshot = snapshots[1];
                var freeGamesSnapshot = snapshots[2];
                var freeGamesAvailable = ReadLong(freeGamesSnapshot, "available");
                var specialPointsSnapshot = snapshots[3];
                var specialPointsAvailable = ReadLong(specialPointsSnapshot, "available");
                var energySnapshot = snapshots[4];
                var energyBalance = ReadLong(energySnapshot, "available");
                if (guardSnapshot.Exists &&
                    guardSnapshot.TryGetValue<Timestamp>("lastSpinStartedAt", out var lastStartedAt))
                {
                    var nextAllowedAtUtc = lastStartedAt.ToDateTime().Add(cooldown);
                    if (nextAllowedAtUtc > startedAtUtc)
                    {
                        return new SlotSpinAdmission(
                            wagerPoints,
                            0,
                            false,
                            checked((int)freeGamesAvailable),
                            false,
                            checked((int)specialPointsAvailable),
                            nextAllowedAtUtc - startedAtUtc,
                            energyBalance,
                            null);
                    }
                }

                var availableCredits = ReadLong(slotsCreditsSnapshot, "available");
                var storedFreeSpinWagerPoints = ReadLong(guardSnapshot, "freeSpinWagerPoints");
                var storedFreeSpinFeatureMode = ReadString(guardSnapshot, "freeSpinFeatureMode");
                if (useFreeSpin && freeGamesAvailable <= 0)
                {
                    throw new NoFreeSpinsException();
                }
                if (useSpecialBoost && specialBoostCost <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(specialBoostCost),
                        "A special boost must have a positive point cost.");
                }
                if (useSpecialBoost && specialPointsAvailable < specialBoostCost)
                {
                    throw new InsufficientSpecialPointsException(
                        specialPointsAvailable,
                        specialBoostCost);
                }

                var effectiveWagerPoints = useFreeSpin
                    ? storedFreeSpinWagerPoints > 0 ? storedFreeSpinWagerPoints : wagerPoints
                    : wagerPoints;
                var activeFreeSpinFeatureMode = useFreeSpin ? storedFreeSpinFeatureMode : null;
                var chargedWagerPoints = useFreeSpin ? 0 : wagerPoints;
                if (!useFreeSpin && availableCredits < wagerPoints)
                {
                    throw new InsufficientSlotCreditsException(availableCredits, wagerPoints);
                }

                var remainingAfterAdmission = useFreeSpin
                    ? freeGamesAvailable - 1
                    : freeGamesAvailable;
                var specialPointsAfterAdmission = useSpecialBoost
                    ? specialPointsAvailable - specialBoostCost
                    : specialPointsAvailable;

                if (useFreeSpin)
                {
                    transaction.Update(freeGamesReference, new Dictionary<string, object>
                    {
                        ["available"] = remainingAfterAdmission,
                        ["version"] = FieldValue.Increment(1),
                        ["updatedAt"] = Timestamp.FromDateTime(startedAtUtc)
                    });
                    transaction.Create(
                        freeGameUseTransactionReference,
                        BalanceTransactionData(
                            freeGameUseTransactionReference.Id,
                            userId,
                            freeGamesCurrencyId,
                            -1,
                            remainingAfterAdmission,
                            "free-game-use",
                            freeGameUseTransactionReference.Id,
                            startedAtUtc));
                }

                if (useSpecialBoost)
                {
                    if (specialPointsSnapshot.Exists)
                    {
                        transaction.Update(specialPointsReference, new Dictionary<string, object>
                        {
                            ["available"] = specialPointsAfterAdmission,
                            ["version"] = FieldValue.Increment(1),
                            ["updatedAt"] = Timestamp.FromDateTime(startedAtUtc)
                        });
                    }
                    else
                    {
                        transaction.Create(
                            specialPointsReference,
                            BalanceData(
                                userId,
                                specialPointsCurrencyId,
                                specialPointsAfterAdmission,
                                startedAtUtc));
                    }
                    transaction.Create(
                        specialPointUseTransactionReference,
                        BalanceTransactionData(
                            specialPointUseTransactionReference.Id,
                            userId,
                            specialPointsCurrencyId,
                            -specialBoostCost,
                            specialPointsAfterAdmission,
                            "special-boost-use",
                            specialPointUseTransactionReference.Id,
                            startedAtUtc));
                }

                transaction.Set(guardReference, new Dictionary<string, object>
                {
                    ["userId"] = userId,
                    ["lastSpinStartedAt"] = Timestamp.FromDateTime(startedAtUtc),
                    ["freeSpinWagerPoints"] = remainingAfterAdmission > 0
                        ? effectiveWagerPoints
                        : 0,
                    ["freeSpinFeatureMode"] = remainingAfterAdmission > 0
                        ? activeFreeSpinFeatureMode ?? string.Empty
                        : string.Empty,
                    ["updatedAt"] = Timestamp.FromDateTime(startedAtUtc)
                }, SetOptions.MergeAll);
                return new SlotSpinAdmission(
                    effectiveWagerPoints,
                    chargedWagerPoints,
                    useFreeSpin,
                    checked((int)remainingAfterAdmission),
                    useSpecialBoost,
                    checked((int)specialPointsAfterAdmission),
                    null,
                    energyBalance,
                    activeFreeSpinFeatureMode);
            },
            cancellationToken: cancellationToken);
    }

    public async Task<SlotStateResponse> GetSlotStateAsync(
        string userId,
        string gameId,
        CancellationToken cancellationToken)
    {
        var guardReference = SlotSpinGuardDocument(userId, gameId);
        var specialPointsCurrencyId = GameCurrencyId(SpecialPointsCurrencyId, gameId);
        var energyCurrencyId = GameCurrencyId(EnergyCurrencyId, gameId);
        var snapshots = await Task.WhenAll(
            guardReference.GetSnapshotAsync(cancellationToken),
            BalanceDocument(userId, specialPointsCurrencyId).GetSnapshotAsync(cancellationToken),
            BalanceDocument(userId, energyCurrencyId).GetSnapshotAsync(cancellationToken));
        var guardSnapshot = snapshots[0];
        return new SlotStateResponse(
            0,
            null,
            checked((int)ReadLong(snapshots[1], "available")),
            ReadLong(snapshots[2], "available"),
            CreateSealCollections(
                ReadLongMap(guardSnapshot, "sealCounts"),
                ReadLongMap(guardSnapshot, "sealWagerTotals")),
            null);
    }

    public async Task<IReadOnlyList<SlotSpinHistoryItem>> GetSlotSpinHistoryAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var snapshots = await database
            .Collection("slotSpinResults")
            .WhereEqualTo("userId", userId)
            .GetSnapshotAsync(cancellationToken);

        return snapshots.Documents
            .Select(snapshot => new SlotSpinHistoryItem(
                snapshot.GetValue<string>("spinId"),
                snapshot.GetValue<string>("gameId"),
                ReadLong(snapshot, "wageredSlotsCredits"),
                ReadLong(snapshot, "wonSlotsCredits"),
                ReadLong(snapshot, "netSlotsCredits"),
                snapshot.GetValue<string>("result"),
                snapshot.GetValue<Timestamp>("createdAt").ToDateTime()))
            .OrderByDescending(spin => spin.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 100))
            .ToArray();
    }

    public async Task InitializeSchemaAsync(CancellationToken cancellationToken)
    {
        var createdAtUtc = DateTime.UtcNow;
        var legacyLoadedMoneyCurrency = CurrencyDocument(LegacyLoadedMoneyCurrencyId);
        var slotsCreditsCurrency = CurrencyDocument(SlotsCreditsCurrencyId);
        var freeGamesCurrency = CurrencyDocument(FreeGamesCurrencyId);
        var specialPointsCurrency = CurrencyDocument(SpecialPointsCurrencyId);
        var energyCurrency = CurrencyDocument(EnergyCurrencyId);
        var legacyLoadedMoneySnapshot = await legacyLoadedMoneyCurrency.GetSnapshotAsync(cancellationToken);
        var currencySnapshots = await Task.WhenAll(
            slotsCreditsCurrency.GetSnapshotAsync(cancellationToken),
            freeGamesCurrency.GetSnapshotAsync(cancellationToken),
            specialPointsCurrency.GetSnapshotAsync(cancellationToken),
            energyCurrency.GetSnapshotAsync(cancellationToken));

        if (legacyLoadedMoneySnapshot.Exists || currencySnapshots.Any(snapshot => !snapshot.Exists))
        {
            var batch = database.StartBatch();
            if (legacyLoadedMoneySnapshot.Exists)
            {
                batch.Delete(legacyLoadedMoneyCurrency);
            }

            if (!currencySnapshots[0].Exists)
            {
                batch.Set(
                    slotsCreditsCurrency,
                    CurrencyData(SlotsCreditsCurrencyId, "Slots credits", 0, createdAtUtc),
                    SetOptions.MergeAll);
            }

            if (!currencySnapshots[1].Exists)
            {
                batch.Set(
                    freeGamesCurrency,
                    CurrencyData(FreeGamesCurrencyId, "Free games", 0, createdAtUtc),
                    SetOptions.MergeAll);
            }

            if (!currencySnapshots[2].Exists)
            {
                batch.Set(
                    specialPointsCurrency,
                    CurrencyData(
                        SpecialPointsCurrencyId,
                        "Wukong power points",
                        0,
                        createdAtUtc),
                    SetOptions.MergeAll);
            }

            if (!currencySnapshots[3].Exists)
            {
                batch.Set(
                    energyCurrency,
                    CurrencyData(EnergyCurrencyId, "Energy", 0, createdAtUtc),
                    SetOptions.MergeAll);
            }

            await batch.CommitAsync(cancellationToken);
        }

        var users = await database.Collection("users").GetSnapshotAsync(cancellationToken);
        foreach (var user in users.Documents)
        {
            await EnsureAccountSchemaAsync(user.Id, cancellationToken);
        }
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

    private DocumentReference UserDocument(string userId) =>
        database.Collection("users").Document(userId);

    private DocumentReference BalanceDocument(string userId, string currencyId) =>
        database.Collection("userBalances").Document($"{userId}_{currencyId}");

    private DocumentReference CurrencyDocument(string currencyId) =>
        database.Collection("currencies").Document(currencyId);

    private DocumentReference BalanceTransactionDocument(string transactionId) =>
        database.Collection("balanceTransactions").Document(transactionId);

    private DocumentReference StatisticsDocument(string userId) =>
        database.Collection("userSlotStatistics").Document(userId);

    private DocumentReference SlotSpinResultDocument(Guid spinId) =>
        database.Collection("slotSpinResults").Document(spinId.ToString("N"));

    private DocumentReference SlotSpinGuardDocument(string userId, string gameId) =>
        database.Collection("slotSpinGuards").Document($"{userId}_{CreateLookupKey(gameId)}");

    private DocumentReference EmailKeyDocument(string email) =>
        database.Collection("accountEmailKeys").Document(CreateLookupKey(email));

    private DocumentReference PlayerNameKeyDocument(string normalizedPlayerName) =>
        database.Collection("accountPlayerNameKeys").Document(CreateLookupKey(normalizedPlayerName));

    private static Dictionary<string, object> UserData(
        StoredAccount account,
        DateTime createdAtUtc) => new()
    {
        ["userId"] = account.Account.UserId,
        ["playerName"] = account.Account.PlayerName,
        ["normalizedPlayerName"] = account.NormalizedPlayerName,
        ["email"] = account.Account.Email,
        ["passwordHash"] = account.PasswordHash,
        ["status"] = account.Status,
        ["deactivated"] = account.Deactivated,
        ["authProvider"] = "firebase-email",
        ["firebaseUid"] = account.Account.UserId,
        ["emailVerified"] = account.Status == "active",
        ["role"] = account.Account.Role,
        ["accountSchemaVersion"] = AccountSchemaVersion,
        ["createdAt"] = Timestamp.FromDateTime(createdAtUtc),
        ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
    };

    private static Dictionary<string, object> BalanceData(
        string userId,
        string currencyId,
        long available,
        DateTime createdAtUtc) => new()
    {
        ["userId"] = userId,
        ["currencyId"] = currencyId,
        ["available"] = available,
        ["reserved"] = 0L,
        ["version"] = 1L,
        ["createdAt"] = Timestamp.FromDateTime(createdAtUtc),
        ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
    };

    private static string GameCurrencyId(string currencyId, string gameId) =>
        $"{currencyId}:{gameId.Replace('/', '_')}";

    private static Dictionary<string, object> CurrencyData(
        string currencyId,
        string name,
        long precision,
        DateTime createdAtUtc) => new()
    {
        ["currencyId"] = currencyId,
        ["name"] = name,
        ["precision"] = precision,
        ["active"] = true,
        ["updatedAt"] = Timestamp.FromDateTime(createdAtUtc)
    };

    private static Dictionary<string, object> BalanceTransactionData(
        string transactionId,
        string userId,
        string currencyId,
        long amount,
        long balanceAfter,
        string type,
        string idempotencyKey,
        DateTime createdAtUtc) => new()
    {
        ["transactionId"] = transactionId,
        ["userId"] = userId,
        ["currencyId"] = currencyId,
        ["amount"] = amount,
        ["balanceAfter"] = balanceAfter,
        ["type"] = type,
        ["idempotencyKey"] = idempotencyKey,
        ["createdAt"] = Timestamp.FromDateTime(createdAtUtc)
    };

    private static Dictionary<string, object> StatisticsData(
        string userId,
        DateTime createdAtUtc) => new()
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

    private static Dictionary<string, object> KeyData(string userId, DateTime createdAtUtc) => new()
    {
        ["userId"] = userId,
        ["createdAt"] = Timestamp.FromDateTime(createdAtUtc)
    };

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

    private Task<StoredAccount?> EnsureAccountSchemaAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var userReference = UserDocument(userId);
        var legacyLoadedMoneyReference = BalanceDocument(userId, LegacyLoadedMoneyCurrencyId);
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
        var migrationTransactionReference = BalanceTransactionDocument(
            $"{userId}-wallet-v1-migration");

        return database.RunTransactionAsync(
            async transaction =>
            {
                var userSnapshot = await transaction.GetSnapshotAsync(
                    userReference,
                    cancellationToken);
                if (!userSnapshot.Exists)
                {
                    return null;
                }

                var legacyLoadedMoneySnapshot = await transaction.GetSnapshotAsync(
                    legacyLoadedMoneyReference,
                    cancellationToken);
                var slotsCreditsSnapshot = await transaction.GetSnapshotAsync(
                    slotsCreditsReference,
                    cancellationToken);
                var freeGamesSnapshot = await transaction.GetSnapshotAsync(
                    freeGamesReference,
                    cancellationToken);
                var specialPointsSnapshot = await transaction.GetSnapshotAsync(
                    specialPointsReference,
                    cancellationToken);
                var energySnapshot = await transaction.GetSnapshotAsync(
                    energyReference,
                    cancellationToken);
                var scopedWukongFreeGamesSnapshot = await transaction.GetSnapshotAsync(
                    scopedWukongFreeGamesReference,
                    cancellationToken);
                var scopedWukongSpecialPointsSnapshot = await transaction.GetSnapshotAsync(
                    scopedWukongSpecialPointsReference,
                    cancellationToken);
                var scopedWukongEnergySnapshot = await transaction.GetSnapshotAsync(
                    scopedWukongEnergyReference,
                    cancellationToken);
                var statisticsSnapshot = await transaction.GetSnapshotAsync(
                    statisticsReference,
                    cancellationToken);
                var migrationTransactionSnapshot = await transaction.GetSnapshotAsync(
                    migrationTransactionReference,
                    cancellationToken);

                var migratedAtUtc = DateTime.UtcNow;
                var accountSchemaVersion = ReadLong(userSnapshot, "accountSchemaVersion");
                var needsAccountMigration =
                    accountSchemaVersion < AccountSchemaVersion ||
                    !userSnapshot.TryGetValue<bool>("deactivated", out _) ||
                    legacyLoadedMoneySnapshot.Exists ||
                    !slotsCreditsSnapshot.Exists ||
                    !freeGamesSnapshot.Exists ||
                    !specialPointsSnapshot.Exists ||
                    !energySnapshot.Exists ||
                    !scopedWukongFreeGamesSnapshot.Exists ||
                    !scopedWukongSpecialPointsSnapshot.Exists ||
                    !scopedWukongEnergySnapshot.Exists;
                var slotsCredits = slotsCreditsSnapshot.Exists
                    ? ReadLong(slotsCreditsSnapshot, "available")
                    : ReadLong(userSnapshot, "balancePoints", LegacySlotsCreditsFallback);
                var freeGames = freeGamesSnapshot.Exists
                    ? ReadLong(freeGamesSnapshot, "available")
                    : 0;
                var specialPoints = specialPointsSnapshot.Exists
                    ? ReadLong(specialPointsSnapshot, "available")
                    : 0;
                var energy = energySnapshot.Exists
                    ? ReadLong(energySnapshot, "available")
                    : 0;

                if (legacyLoadedMoneySnapshot.Exists)
                {
                    transaction.Delete(legacyLoadedMoneyReference);
                }

                if (!slotsCreditsSnapshot.Exists)
                {
                    transaction.Create(
                        slotsCreditsReference,
                        BalanceData(userId, SlotsCreditsCurrencyId, slotsCredits, migratedAtUtc));
                }

                if (!freeGamesSnapshot.Exists)
                {
                    transaction.Create(
                        freeGamesReference,
                        BalanceData(userId, FreeGamesCurrencyId, freeGames, migratedAtUtc));
                }

                if (!specialPointsSnapshot.Exists)
                {
                    transaction.Create(
                        specialPointsReference,
                        BalanceData(userId, SpecialPointsCurrencyId, 0, migratedAtUtc));
                }

                if (!energySnapshot.Exists)
                {
                    transaction.Create(
                        energyReference,
                        BalanceData(userId, EnergyCurrencyId, 0, migratedAtUtc));
                }

                if (!scopedWukongFreeGamesSnapshot.Exists)
                {
                    transaction.Create(
                        scopedWukongFreeGamesReference,
                        BalanceData(
                            userId,
                            scopedWukongFreeGamesCurrencyId,
                            freeGames,
                            migratedAtUtc));
                }

                if (!scopedWukongSpecialPointsSnapshot.Exists)
                {
                    transaction.Create(
                        scopedWukongSpecialPointsReference,
                        BalanceData(
                            userId,
                            scopedWukongSpecialPointsCurrencyId,
                            specialPoints,
                            migratedAtUtc));
                }

                if (!scopedWukongEnergySnapshot.Exists)
                {
                    transaction.Create(
                        scopedWukongEnergyReference,
                        BalanceData(
                            userId,
                            scopedWukongEnergyCurrencyId,
                            energy,
                            migratedAtUtc));
                }

                if (!statisticsSnapshot.Exists)
                {
                    transaction.Create(statisticsReference, StatisticsData(userId, migratedAtUtc));
                }

                if (!slotsCreditsSnapshot.Exists && !migrationTransactionSnapshot.Exists)
                {
                    transaction.Create(
                        migrationTransactionReference,
                        BalanceTransactionData(
                            migrationTransactionReference.Id,
                            userId,
                            SlotsCreditsCurrencyId,
                            slotsCredits,
                            slotsCredits,
                            "wallet-v1-migration",
                            migrationTransactionReference.Id,
                            migratedAtUtc));
                }

                if (needsAccountMigration)
                {
                    transaction.Set(userReference, new Dictionary<string, object>
                    {
                        ["lifetimeLoadedMoney"] = FieldValue.Delete,
                        ["walletSchemaVersion"] = FieldValue.Delete,
                        ["deactivated"] = userSnapshot.TryGetValue<bool>(
                            "deactivated",
                            out var deactivated) && deactivated,
                        ["accountSchemaVersion"] = AccountSchemaVersion,
                        ["updatedAt"] = Timestamp.FromDateTime(migratedAtUtc)
                    }, SetOptions.MergeAll);
                }

                return ToStoredAccount(
                    userSnapshot,
                    slotsCredits,
                    freeGames,
                    statisticsSnapshot.Exists
                        ? ToSlotStatistics(statisticsSnapshot)
                        : EmptySlotStatistics());
            },
            cancellationToken: cancellationToken);
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
            ReadLong(slotsCreditsSnapshot, "available"),
            ReadLong(freeGamesSnapshot, "available"),
            ToSlotStatistics(statisticsSnapshot));
    }

    private static StoredAccount? ToStoredAccount(
        DocumentSnapshot userSnapshot,
        long slotsCredits,
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

    private static SlotStatistics ToSlotStatistics(DocumentSnapshot snapshot) => new(
        ReadLong(snapshot, "spinsPlayed"),
        ReadLong(snapshot, "wins"),
        ReadLong(snapshot, "losses"),
        ReadLong(snapshot, "creditsWagered"),
        ReadLong(snapshot, "creditsWon"),
        ReadLong(snapshot, "netCredits"));

    private static SlotStatistics EmptySlotStatistics() => new(0, 0, 0, 0, 0, 0);

    private static long ReadLong(
        DocumentSnapshot snapshot,
        string field,
        long fallback = 0) =>
        snapshot.Exists && snapshot.TryGetValue<long>(field, out var value) ? value : fallback;

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

    private static SealCollectionSettlement SettleSealCollections(
        DocumentSnapshot guardSnapshot,
        SpinResult result,
        bool energyCompleted)
    {
        var counts = ReadLongMap(guardSnapshot, "sealCounts");
        var wagerTotals = ReadLongMap(guardSnapshot, "sealWagerTotals");
        var changed = false;

        foreach (var awarded in result.SealsAwarded)
        {
            if (!SealFeatureModesBySymbolId.TryGetValue(awarded.Key, out var featureMode) ||
                awarded.Value <= 0)
            {
                continue;
            }

            counts[featureMode] = checked(counts[featureMode] + awarded.Value);
            wagerTotals[featureMode] = checked(wagerTotals[featureMode] + result.WagerPoints * awarded.Value);
            changed = true;
        }

        if (energyCompleted)
        {
            var nearestMode = SealFeatureModes
                .OrderByDescending(mode => Math.Min(counts[mode], SealCompletionTarget - 1))
                .ThenBy(mode => Array.IndexOf(SealFeatureModes, mode))
                .First();
            var missing = Math.Max(1, SealCompletionTarget - counts[nearestMode]);
            counts[nearestMode] = checked(counts[nearestMode] + missing);
            wagerTotals[nearestMode] = checked(wagerTotals[nearestMode] + result.WagerPoints * missing);
            changed = true;
        }

        var freeSpinsAwarded = 0;
        string? freeSpinFeatureMode = null;
        var freeSpinWagerPoints = 0L;

        foreach (var mode in SealFeatureModes)
        {
            if (counts[mode] < SealCompletionTarget)
            {
                continue;
            }

            var completedCount = Math.Max(1, counts[mode]);
            var averageWager = DivideRounded(wagerTotals[mode], completedCount);
            freeSpinsAwarded = checked(freeSpinsAwarded + SealCompletionFreeSpins);
            freeSpinFeatureMode ??= mode;
            if (freeSpinWagerPoints <= 0)
            {
                freeSpinWagerPoints = averageWager > 0 ? averageWager : result.WagerPoints;
            }

            var overflow = counts[mode] - SealCompletionTarget;
            counts[mode] = Math.Max(0, overflow);
            wagerTotals[mode] = counts[mode] > 0
                ? checked(averageWager * counts[mode])
                : 0;
            changed = true;
        }

        return new SealCollectionSettlement(
            counts,
            wagerTotals,
            freeSpinsAwarded,
            freeSpinFeatureMode,
            freeSpinWagerPoints,
            CreateSealCollections(counts, wagerTotals),
            changed);
    }

    private static IReadOnlyList<SlotSealCollection> CreateSealCollections(
        IReadOnlyDictionary<string, long> counts,
        IReadOnlyDictionary<string, long> wagerTotals) =>
        SealFeatureModes
            .Select(mode =>
            {
                var count = Math.Min(
                    SealCompletionTarget,
                    Math.Max(0, counts.TryGetValue(mode, out var rawCount) ? rawCount : 0));
                var wagerTotal = Math.Max(0, wagerTotals.TryGetValue(mode, out var rawTotal) ? rawTotal : 0);
                var averageWager = count <= 0 ? 0 : DivideRounded(wagerTotal, count);
                return new SlotSealCollection(
                    mode,
                    checked((int)count),
                    averageWager,
                    SealCompletionTarget);
            })
            .ToArray();

    private static long DivideRounded(long total, long count) =>
        count <= 0 ? 0 : checked((long)Math.Round(total / (decimal)count, MidpointRounding.AwayFromZero));

    private sealed record SealCollectionSettlement(
        IReadOnlyDictionary<string, long> SealCounts,
        IReadOnlyDictionary<string, long> SealWagerTotals,
        int FreeSpinsAwarded,
        string? FreeSpinFeatureMode,
        long FreeSpinWagerPoints,
        IReadOnlyList<SlotSealCollection> Collections,
        bool SealsChanged);

    private async Task RevokeSessionsAsync(
        Query query,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken)
    {
        var snapshots = await query.GetSnapshotAsync(cancellationToken);
        foreach (var chunk in snapshots.Documents.Chunk(450))
        {
            var batch = database.StartBatch();
            foreach (var snapshot in chunk)
            {
                batch.Update(snapshot.Reference, new Dictionary<string, object>
                {
                    ["revoked"] = true,
                    ["revokedAt"] = Timestamp.FromDateTime(revokedAtUtc)
                });
            }

            await batch.CommitAsync(cancellationToken);
        }
    }

    private static string CreateLookupKey(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(digest);
    }
}
