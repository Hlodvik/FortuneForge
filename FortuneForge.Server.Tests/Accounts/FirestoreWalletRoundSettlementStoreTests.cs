using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Accounts;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class FirestoreWalletRoundSettlementStoreTests(SolitaireFirestoreEmulatorFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentRetriesPostBothLedgerLegsOnceAndReturnTheOriginalReceipt()
    {
        var database = CreateDatabase();
        var store = new FirestoreWalletRoundSettlementStore(database);
        var operation = Operation();
        await SeedWallet(database, operation.UserId, 611);

        var results = await Task.WhenAll(
            store.SettleAsync(operation, Now.AddTicks(1), default),
            store.SettleAsync(operation, Now.AddTicks(1), default));
        var first = Assert.Single(results, result => !result.WasAlreadySettled);
        Assert.Equal(first with { WasAlreadySettled = true }, Assert.Single(results, result => result.WasAlreadySettled));
        Assert.False(first.InsufficientFunds);
        Assert.Equal(Now, first.SettledAtUtc);
        Assert.Equal(611L, first.BalanceBeforeCents);
        Assert.Equal(366L, first.BalanceAfterWagerCents);
        Assert.Equal(755L, first.BalanceAfterCents);
        await AssertWallet(database, operation.UserId, 755, 2);

        var ledger = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(2, ledger.Count);
        var wager = Assert.Single(ledger, row => row.Id == first.WagerTransactionId);
        var payout = Assert.Single(ledger, row => row.Id == first.PayoutTransactionId);
        Assert.Equal(-245L, wager.GetValue<long>("amountCents"));
        Assert.Equal(366L, wager.GetValue<long>("balanceAfterCents"));
        Assert.Equal(389L, payout.GetValue<long>("amountCents"));
        Assert.Equal(755L, payout.GetValue<long>("balanceAfterCents"));
        Assert.Equal(first.SettlementId, payout.GetValue<string>("walletSettlementId"));

        await SeedWallet(database, operation.UserId, 900, version: 3);
        Assert.Equal(first with { WasAlreadySettled = true }, await store.SettleAsync(operation, Now.AddMinutes(1), default));
        await AssertWallet(database, operation.UserId, 900, 3);

        await payout.Reference.UpdateAsync("amountCents", 390L);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SettleAsync(operation, Now.AddMinutes(1), default));
        await AssertWallet(database, operation.UserId, 900, 3);
    }

    [Fact]
    public async Task GrossWagerDeclineRemainsRejectedAfterTheWalletIsFunded()
    {
        var database = CreateDatabase();
        var store = new FirestoreWalletRoundSettlementStore(database);
        var operation = Operation() with { PayoutCents = 1_000 };
        await SeedWallet(database, operation.UserId, 200);

        var first = await store.SettleAsync(operation, Now, default);

        Assert.True(first.InsufficientFunds);
        Assert.False(first.WasAlreadySettled);
        Assert.Equal(200L, first.BalanceBeforeCents);
        Assert.Equal(200L, first.BalanceAfterWagerCents);
        Assert.Equal(200L, first.BalanceAfterCents);
        Assert.Null(first.WagerTransactionId);
        Assert.Null(first.PayoutTransactionId);
        await AssertWallet(database, operation.UserId, 200, 1);

        await SeedWallet(database, operation.UserId, 500, version: 2);
        Assert.Equal(first with { WasAlreadySettled = true }, await store.SettleAsync(operation, Now.AddDays(1), default));
        await Assert.ThrowsAsync<WalletRoundSettlementConflictException>(() =>
            store.SettleAsync(operation with { PayoutCents = 999 }, Now, default));
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        await AssertWallet(database, operation.UserId, 500, 2);
    }

    [Fact]
    public async Task StartingBalanceMinimumBindsStoreTermsAndCannotReduceTheGrossWagerRequirement()
    {
        var database = CreateDatabase();
        var store = new FirestoreWalletRoundSettlementStore(database);
        var operation = Operation() with { MinimumBalanceBeforeCents = 650 };
        await SeedWallet(database, operation.UserId, 611);

        var decline = await store.SettleAsync(operation, Now, default);
        Assert.True(decline.InsufficientFunds);
        Assert.Equal(611L, decline.BalanceAfterCents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        await AssertWallet(database, operation.UserId, 611, 1);
        var saved = await database.Collection("walletRoundSettlements").Document(decline.SettlementId).GetSnapshotAsync();
        Assert.Equal(650L, saved.GetValue<long>("minimumBalanceBeforeCents"));
        await Assert.ThrowsAsync<WalletRoundSettlementConflictException>(() =>
            store.SettleAsync(operation with { MinimumBalanceBeforeCents = null }, Now, default));
        await Assert.ThrowsAsync<WalletRoundSettlementConflictException>(() =>
            store.SettleAsync(operation with { MinimumBalanceBeforeCents = 651 }, Now, default));

        var lowerMinimum = operation with { OperationId = "gross-wager-still-required", WagerCents = 612, MinimumBalanceBeforeCents = 10 };
        Assert.True((await store.SettleAsync(lowerMinimum, Now, default)).InsufficientFunds);
        await AssertWallet(database, operation.UserId, 611, 1);
    }

    [Fact]
    public async Task AcceptedReservationRetainsItsMinimumAndCompletesBelowIt()
    {
        var database = CreateDatabase();
        var store = new FirestoreWalletRoundSettlementStore(database);
        var hold = Operation() with { PayoutCents = 0, Phase = GameWalletSettlementPhase.Reserve, MinimumBalanceBeforeCents = 611 };
        await SeedWallet(database, hold.UserId, 611);

        var reserved = await store.SettleAsync(hold, Now, default);
        Assert.False(reserved.InsufficientFunds);
        Assert.Equal(366L, reserved.BalanceAfterCents);
        var completion = hold with
        {
            Phase = GameWalletSettlementPhase.CompleteReservation,
            OutcomeSha256 = new string('b', 64), ReservationSha256 = hold.OutcomeSha256,
            WagerCents = 0, PayoutCents = 389, MinimumBalanceBeforeCents = null,
        };
        await Assert.ThrowsAsync<ArgumentException>(() => store.SettleAsync(
            completion with { MinimumBalanceBeforeCents = 611 }, Now, default));

        var completed = await store.SettleAsync(completion, Now.AddMinutes(1), default);
        Assert.False(completed.InsufficientFunds);
        Assert.Equal(366L, completed.BalanceBeforeCents);
        Assert.Equal(755L, completed.BalanceAfterCents);
        Assert.Equal(reserved with { WasAlreadySettled = true }, await store.SettleAsync(hold, Now.AddMinutes(2), default));
        Assert.Equal(completed with { WasAlreadySettled = true }, await store.SettleAsync(completion, Now.AddMinutes(2), default));
        Assert.Equal(2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        await AssertWallet(database, hold.UserId, 755, 3);
    }

    [Fact]
    public async Task SettlementKeysAreScopedToGameAndPlayerAndBindTheOutcome()
    {
        var database = CreateDatabase();
        var store = new FirestoreWalletRoundSettlementStore(database);
        var operation = Operation() with { WagerCents = 0, PayoutCents = 0 };
        await SeedWallet(database, operation.UserId, 0);
        await SeedWallet(database, "other-player", 0);

        var first = await store.SettleAsync(operation, Now, default);
        var otherGame = await store.SettleAsync(operation with { GameId = "other-game" }, Now, default);
        var otherPlayer = await store.SettleAsync(operation with { UserId = "other-player" }, Now, default);

        Assert.Equal(3, new[] { first.SettlementId, otherGame.SettlementId, otherPlayer.SettlementId }.Distinct().Count());
        Assert.False(first.InsufficientFunds);
        Assert.Null(first.WagerTransactionId);
        Assert.Null(first.PayoutTransactionId);
        await Assert.ThrowsAsync<WalletRoundSettlementConflictException>(() =>
            store.SettleAsync(operation with { OutcomeSha256 = new string('b', 64) }, Now, default));
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        await AssertWallet(database, operation.UserId, 0, 1);
    }

    [Theory]
    [InlineData(121L, 239L, 618L)]
    [InlineData(0L, 0L, 500L)]
    public async Task LegacySlotLedgerAndGameStateCommitTogetherWithTheExistingSchema(long wagerCents, long payoutCents, long finalCents)
    {
        var database = CreateDatabase();
        var spin = Guid.NewGuid();
        var operation = Operation() with { OperationId = spin.ToString("N"), WagerCents = wagerCents, PayoutCents = payoutCents };
        var layout = WalletRoundLedgerLayout.LegacySlot(spin);
        var game = database.Collection("slotSpinResults").Document(operation.OperationId);
        await Wallet(database, operation.UserId).SetAsync(new Dictionary<string, object> { ["available"] = 5L, ["version"] = 2L });
        await game.SetAsync(new Dictionary<string, object> { ["status"] = "prepared" });

        Task<WalletRoundSettlementReceipt> Apply(bool commit) => database.RunTransactionAsync(async transaction =>
        {
            var result = await transaction.GetSnapshotAsync(game);
            Assert.Equal("prepared", result.GetValue<string>("status"));
            var receipt = await FirestoreWalletRoundSettlementStore.StageAsync(database, transaction, operation, Now, default, layout);
            transaction.Update(game, "status", "settled");
            if (!commit)
                throw new InvalidOperationException("Stop the game transaction before commit.");
            return receipt;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => Apply(false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.RunTransactionAsync(transaction =>
            FirestoreWalletRoundSettlementStore.StageAsync(database, transaction, operation, Now, default, layout, requireExisting: true)));
        Assert.Equal("prepared", (await game.GetSnapshotAsync()).GetValue<string>("status"));
        Assert.Equal(5L, (await Wallet(database, operation.UserId).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);

        var settled = await Apply(true);

        Assert.Equal("settled", (await game.GetSnapshotAsync()).GetValue<string>("status"));
        Assert.Equal(finalCents, settled.BalanceAfterCents);
        Assert.Equal(wagerCents > 0 ? $"{spin:N}-wager" : null, settled.WagerTransactionId);
        Assert.Equal(payoutCents > 0 ? $"{spin:N}-payout" : null, settled.PayoutTransactionId);
        await AssertWallet(database, operation.UserId, finalCents, 3);
        var ledgers = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(wagerCents > 0 ? 2 : 0, ledgers.Count);
        foreach (var ledger in ledgers)
        {
            var isWager = ledger.Id == settled.WagerTransactionId;
            Assert.Equal(new[] { "amount", "balanceAfter", "createdAt", "currencyId", "idempotencyKey", "transactionId", "type", "userId" },
                ledger.ToDictionary().Keys.Order(StringComparer.Ordinal));
            Assert.Equal(ledger.Id, ledger.GetValue<string>("idempotencyKey"));
            Assert.Equal(isWager ? "slot-wager" : "slot-payout", ledger.GetValue<string>("type"));
            Assert.Equal((isWager ? -wagerCents : payoutCents) / 100d, ledger.GetValue<double>("amount"));
        }
        var replay = await database.RunTransactionAsync(transaction =>
            FirestoreWalletRoundSettlementStore.StageAsync(database, transaction, operation, Now.AddMinutes(1), default, layout, requireExisting: true));
        Assert.Equal(settled with { WasAlreadySettled = true }, replay);
    }

    private FirestoreDb CreateDatabase()
    {
        _ = fixture;
        return new FirestoreDbBuilder
        {
            ProjectId = $"demo-wallet-round-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
    }

    private static WalletRoundSettlement Operation() => new("test-game", "player", "operation", new string('a', 64), 245, 389);

    private static DocumentReference Wallet(FirestoreDb database, string userId) =>
        database.Collection("userBalances").Document($"{userId}_slotsCredits");

    private static Task SeedWallet(FirestoreDb database, string userId, long cents, long version = 1) =>
        Wallet(database, userId).SetAsync(new Dictionary<string, object>
        {
            ["userId"] = userId,
            ["currencyId"] = "slotsCredits",
            ["available"] = cents / 100,
            ["availableFractionalCents"] = cents % 100,
            ["reserved"] = 0L,
            ["version"] = version,
        });

    private static async Task AssertWallet(FirestoreDb database, string userId, long cents, long version)
    {
        var snapshot = await Wallet(database, userId).GetSnapshotAsync();
        Assert.Equal(cents / 100, snapshot.GetValue<long>("available"));
        Assert.Equal(cents % 100, snapshot.GetValue<long>("availableFractionalCents"));
        Assert.Equal(version, snapshot.GetValue<long>("version"));
    }
}
