using FortuneForge.Games.GodsOfOlympus;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Wukong;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Slots.Bonuses;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

[Collection(MidnightVaultPaidEmulatorCollection.Name)]
public sealed class LegacySlotRecoveryFirestoreTests(MidnightVaultPaidEmulatorFixture emulator)
{
    private const string Account = "old-classic-player";
    private static readonly DateTime Start = new(2026, 11, 21, 2, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Expiry = SlotSpinGuard.ActiveAdmissionTimeout;

    [Fact]
    public async Task LastProvenLegacyCreditAndBoostAreRestoredExactlyOnceOnTheHistoricalWukongKey()
    {
        var database = emulator.Database();
        var store = await CreatePlayerAsync(database);
        const string game = WukongEngine.GameId;
        const string scope = WukongEngine.LegacyStorageGameId;
        var guard = Guard(database, scope);
        await guard.SetAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 75L, ["freeSpinFeatureMode"] = "sync", ["consecutiveFiveMisses"] = 7L
        });
        var free = await SeedUnitsAsync(database, $"freeGames:{scope}", 1);
        var special = await SeedUnitsAsync(database, $"specialPoints:{scope}", 9);
        var admitted = await store.BeginSlotSpinAsync(Account, game, 4, 25, true, true, 8, Start, TimeSpan.Zero, default);
        Assert.Equal(3, admitted.WagerPoints);
        Assert.Equal(0, (await free.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(1, (await special.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(LegacySlotRecoveryResult.Active, await store.ReconcileExpiredLegacySlotAsync(Account, game, Start.AddSeconds(1), default));

        var now = Start + Expiry + TimeSpan.FromMilliseconds(5);
        Assert.Equal(LegacySlotRecoveryResult.Reconciled, await store.ReconcileExpiredLegacySlotAsync(Account, game, now, default));
        Assert.Equal(LegacySlotRecoveryResult.NoLegacyAdmission, await store.ReconcileExpiredLegacySlotAsync(Account, game, now, default));
        Assert.Equal(1, (await free.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(9, (await special.GetSnapshotAsync()).GetValue<long>("available"));
        var recoveredGuard = await guard.GetSnapshotAsync();
        Assert.Equal(string.Empty, recoveredGuard.GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
        Assert.Equal(75, recoveredGuard.GetValue<long>("freeSpinWagerCents"));
        Assert.Equal("sync", recoveredGuard.GetValue<string>("freeSpinFeatureMode"));
        Assert.Equal(7, recoveredGuard.GetValue<long>("consecutiveFiveMisses"));
        var receipt = await Admission(database, admitted.AdmissionId).GetSnapshotAsync();
        Assert.Equal("recovered", receipt.GetValue<string>("status"));
        Assert.Equal("expired-unkeyed-classic", receipt.GetValue<string>("recoveryKind"));
        await AssertRefundAsync(database, admitted.AdmissionId, "free-game", $"freeGames:{scope}", 1);
        await AssertRefundAsync(database, admitted.AdmissionId, "special-boost", $"specialPoints:{scope}", 8);
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
        Assert.Equal(100, (await Cash(database).GetSnapshotAsync()).GetValue<long>("available"));
    }

    [Fact]
    public async Task AnExpiredCashAdmissionWithNoBenefitsDoesNotRefundUnchargedCashOrANewerHostAdmission()
    {
        var database = emulator.Database();
        var store = await CreatePlayerAsync(database);
        const string game = SlotSpecialRoundProfiles.RoyalDrawGameId;
        var special = await SeedUnitsAsync(database, $"specialPoints:{game}", 9);
        var first = await store.BeginSlotSpinAsync(Account, game, 6, 25, false, true, 8, Start, TimeSpan.Zero, default);
        var later = Start + Expiry + TimeSpan.FromSeconds(1);
        var current = await store.BeginSlotSpinAsync(Account, game, 8, 25, false, false, 0, later, TimeSpan.Zero, default);

        Assert.Equal(LegacySlotRecoveryResult.Active,
            await store.ReconcileExpiredLegacySlotAsync(Account, game, later.AddSeconds(1), default));
        Assert.Equal(current.AdmissionId.ToString("N"), (await Guard(database, game).GetSnapshotAsync()).GetValue<string>(SlotSpinGuard.ActiveAdmissionField));
        Assert.Equal(LegacySlotRecoveryResult.Reconciled,
            await store.ReconcileExpiredLegacySlotAsync(Account, game, later + Expiry, default));
        Assert.Equal("recovered", (await Admission(database, current.AdmissionId).GetSnapshotAsync()).GetValue<string>("status"));
        Assert.Equal("admitted", (await Admission(database, first.AdmissionId).GetSnapshotAsync()).GetValue<string>("status"));
        Assert.Equal(1, (await special.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(100, (await Cash(database).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task AmbiguousLastCreditAndInconsistentDebitEvidenceCannotWriteARefund()
    {
        var database = emulator.Database();
        var store = await CreatePlayerAsync(database);
        const string game = SlotSpecialRoundProfiles.ReelRichesGameId;
        var free = await SeedUnitsAsync(database, $"freeGames:{game}", 1);
        var last = await store.BeginSlotSpinAsync(Account, game, 4, 25, true, false, 0, Start, TimeSpan.Zero, default);
        var recoverAt = Start + Expiry;
        Assert.Equal(LegacySlotRecoveryResult.ManualReviewRequired,
            await store.ReconcileExpiredLegacySlotAsync(Account, game, recoverAt, default));
        Assert.Equal(0, (await free.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal("admitted", (await Admission(database, last.AdmissionId).GetSnapshotAsync()).GetValue<string>("status"));

        const string otherGame = SlotSpecialRoundProfiles.HighNoonFortuneGameId;
        await Guard(database, otherGame).SetAsync(new Dictionary<string, object> { ["freeSpinWagerCents"] = 75L });
        var otherFree = await SeedUnitsAsync(database, $"freeGames:{otherGame}", 2);
        var admitted = await store.BeginSlotSpinAsync(Account, otherGame, 4, 25, true, false, 0, Start, TimeSpan.Zero, default);
        var stored = await Admission(database, admitted.AdmissionId).GetSnapshotAsync();
        var use = stored.GetValue<string>("freeGameUseTransactionId");
        await database.Collection("balanceTransactions").Document(use).UpdateAsync("amount", -2L);
        Assert.Equal(LegacySlotRecoveryResult.ManualReviewRequired,
            await store.ReconcileExpiredLegacySlotAsync(Account, otherGame, recoverAt, default));
        Assert.Equal(1, (await otherFree.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.False((await database.Collection("balanceTransactions").Document($"{admitted.AdmissionId:N}-free-game-refund").GetSnapshotAsync()).Exists);
    }

    [Fact]
    public async Task ARecordedSpinOrWorkerOwnerFencesTheLegacyReconciler()
    {
        var database = emulator.Database();
        var store = await CreatePlayerAsync(database);
        const string game = SlotSpecialRoundProfiles.PiratesFortuneGameId;
        var admitted = await store.BeginSlotSpinAsync(Account, game, 4, 25, false, false, 0, Start, TimeSpan.Zero, default);
        await database.Collection("slotSpinResults").Document(Guid.NewGuid().ToString("N")).CreateAsync(new Dictionary<string, object>
        {
            ["admissionId"] = admitted.AdmissionId.ToString("N"), ["userId"] = Account, ["gameId"] = game
        });
        var now = Start + Expiry;
        Assert.Equal(LegacySlotRecoveryResult.ManualReviewRequired,
            await store.ReconcileExpiredLegacySlotAsync(Account, game, now, default));
        await Guard(database, game).UpdateAsync("piratesFortuneRoundOwner", game);
        Assert.Equal(LegacySlotRecoveryResult.NoLegacyAdmission,
            await store.ReconcileExpiredLegacySlotAsync(Account, game, now, default));
        Assert.Equal(LegacySlotRecoveryResult.Unsupported,
            await store.ReconcileExpiredLegacySlotAsync(Account, SlotSpecialRoundProfiles.NeonNightsGameId, now, default));
    }

    [Theory]
    [InlineData(OlympusTumbleRules.EngineVersionV2, true)]
    [InlineData(OlympusTumbleRules.EngineVersionV3, true)]
    [InlineData(OlympusTumbleRules.EngineVersion, true)]
    [InlineData(OlympusTumbleRules.LegacyEngineVersion, false)]
    [InlineData("unknown-tumble-rules", false)]
    public async Task OlympusCashUsesOnlyASupportedUnchargedTumbleAdmission(string engineVersion, bool supported)
    {
        var database = emulator.Database();
        var store = await CreatePlayerAsync(database);
        const string game = GodsOfOlympusEngine.GameId;
        var energy = await SeedUnitsAsync(database, $"energy:{game}", 43);
        var admitted = await store.BeginSlotSpinAsync(Account, game, 6, 25, false, false, 0, Start, TimeSpan.Zero, default);
        var saved = Admission(database, admitted.AdmissionId);
        await saved.UpdateAsync("mathModelId", engineVersion);
        var result = await store.ReconcileExpiredLegacySlotAsync(Account, game, Start + Expiry, default);

        Assert.Equal(supported ? LegacySlotRecoveryResult.Reconciled : LegacySlotRecoveryResult.ManualReviewRequired, result);
        Assert.Equal(supported ? "recovered" : "admitted", (await saved.GetSnapshotAsync()).GetValue<string>("status"));
        Assert.Equal(43, (await energy.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(100, (await Cash(database).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Empty((await database.Collection("slotSpinResults").GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OlympusClassicFreeCreditRequiresItsProvenLockAndNoTumbleModel(bool unexpectedTumbleModel)
    {
        var database = emulator.Database();
        var store = await CreatePlayerAsync(database);
        const string game = GodsOfOlympusEngine.GameId;
        var guard = Guard(database, game);
        await guard.SetAsync(new Dictionary<string, object>
        {
            ["freeSpinWagerCents"] = 75L, ["freeSpinFeatureMode"] = "paw-rand"
        });
        var currency = $"freeGames:{game}";
        var free = await SeedUnitsAsync(database, currency, 1);
        await SeedUnitsAsync(database, $"energy:{game}", 31);
        var admitted = await store.BeginSlotSpinAsync(Account, game, 4, 25, true, false, 0, Start, TimeSpan.Zero, default);
        Assert.Equal(3, admitted.WagerPoints);
        if (unexpectedTumbleModel)
            await Admission(database, admitted.AdmissionId).UpdateAsync("mathModelId", OlympusTumbleRules.EngineVersion);
        var result = await store.ReconcileExpiredLegacySlotAsync(Account, game, Start + Expiry, default);

        if (unexpectedTumbleModel)
        {
            Assert.Equal(LegacySlotRecoveryResult.ManualReviewRequired, result);
            Assert.Equal(0, (await free.GetSnapshotAsync()).GetValue<long>("available"));
            return;
        }
        Assert.Equal(LegacySlotRecoveryResult.Reconciled, result);
        Assert.Equal(1, (await free.GetSnapshotAsync()).GetValue<long>("available"));
        var recovered = await guard.GetSnapshotAsync();
        Assert.Equal(75, recovered.GetValue<long>("freeSpinWagerCents"));
        Assert.Equal("paw-rand", recovered.GetValue<string>("freeSpinFeatureMode"));
        await AssertRefundAsync(database, admitted.AdmissionId, "free-game", currency, 1);
    }

    private static async Task<FirestoreAccountStore> CreatePlayerAsync(FirestoreDb database)
    {
        var store = new FirestoreAccountStore(database);
        Assert.NotNull((await store.CreateAsync(Account, Account, Account.ToUpperInvariant(), Account + "@example.test",
            "unused-local-test", "active", default)).Value);
        await Cash(database).UpdateAsync(new Dictionary<string, object> { ["available"] = 100L, ["availableFractionalCents"] = 0L });
        return store;
    }

    private static async Task<DocumentReference> SeedUnitsAsync(FirestoreDb database, string currency, long amount)
    {
        var balance = database.Collection("userBalances").Document($"{Account}_{currency}");
        await balance.SetAsync(new Dictionary<string, object>
        {
            ["userId"] = Account, ["currencyId"] = currency, ["available"] = amount, ["version"] = 0L
        });
        return balance;
    }

    private static DocumentReference Guard(FirestoreDb database, string storageGame) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(Account, storageGame));

    private static DocumentReference Admission(FirestoreDb database, Guid id) =>
        database.Collection("slotSpinAdmissions").Document(id.ToString("N"));

    private static DocumentReference Cash(FirestoreDb database) => database.Collection("userBalances").Document($"{Account}_slotsCredits");

    private static async Task AssertRefundAsync(FirestoreDb database, Guid admissionId, string kind, string currency, long amount)
    {
        var refund = await database.Collection("balanceTransactions").Document($"{admissionId:N}-{kind}-refund").GetSnapshotAsync();
        Assert.Equal(amount, refund.GetValue<long>("amount"));
        Assert.Equal(currency, refund.GetValue<string>("currencyId"));
        Assert.Equal(admissionId.ToString("N"), refund.GetValue<string>("idempotencyKey"));
    }
}
