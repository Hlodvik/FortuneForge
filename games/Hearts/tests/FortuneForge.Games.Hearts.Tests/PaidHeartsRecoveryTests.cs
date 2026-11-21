using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Server.Games.Hearts;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Hearts;

public sealed partial class PaidHeartsFirestoreTests
{
    [Fact]
    public async Task Rejoining_overdue_queue_freezes_original_refund_and_idle_reply()
    {
        var (db, _, roster) = Create(); await Seed(db, "alice"); await Seed(db, "bruno");
        var bank = new PaidHeartsTestBank(db);
        var store = PaidHeartsTestStore.Create(db, roster, bank: bank);
        await store.JoinAsync("alice", "Alice", 1m, "alice-entry", Start, default);
        await store.JoinAsync("bruno", "Bruno", 1m, "bruno-entry", Start, default);
        var expired = await store.JoinAsync("alice", "Alice", 1m, "rejoin-overdue", Start.AddMinutes(2), default);
        Assert.Equal("idle", expired.Kind);
        Assert.Equal(100m, expired.BalanceCredits);
        Assert.Equal(100m, await Credits(db, "alice"));
        Assert.Equal(100m, await Credits(db, "bruno"));
        Assert.Equal(4, (await db.Collection(FirestorePaidHeartsStore.WalletJournalCollection).GetSnapshotAsync()).Count);
        var calls = bank.Calls;
        bank.FailProfileReads = true; bank.FailSettlements = true;
        var replay = await PaidHeartsTestStore.Create(db, roster, bank: bank, admits: () => false)
            .JoinAsync("alice", "", 1m, "rejoin-overdue", Start.AddHours(1), default);
        Assert.Equal(JsonSerializer.Serialize(expired, Json), JsonSerializer.Serialize(replay, Json));
        Assert.Equal(calls, bank.Calls);
    }

    [Fact]
    public async Task Finalized_join_reply_bypasses_later_pending_refund_and_offline_wallet()
    {
        var (db, _, roster) = Create(); await Seed(db, "alice");
        var bank = new PaidHeartsTestBank(db);
        var store = PaidHeartsTestStore.Create(db, roster, bank: bank);
        var original = await store.JoinAsync("alice", "Alice", 1m, "cached-entry", Start, default);
        bank.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.CommandAsync("alice", original.Queue!.MatchId,
            "leave", original.Version, null, "pending-refund", Start, default));
        bank.FailSettlements = true; bank.FailProfileReads = true;
        var restarted = PaidHeartsTestStore.Create(db, roster, bank: bank, admits: () => false);
        var replay = await restarted.JoinAsync("alice", "", 1m, "cached-entry", Start.AddHours(1), default);
        Assert.Equal(JsonSerializer.Serialize(original, Json), JsonSerializer.Serialize(replay, Json));
        Assert.NotEmpty((await db.Collection(FirestorePaidHeartsStore.LocksCollection).GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => restarted.SessionAsync("alice", Start, default));
        bank.FailSettlements = false; bank.FailProfileReads = false;
        Assert.Equal("idle", (await restarted.SessionAsync("alice", Start, default)).Kind);
        Assert.Equal(2, (await db.Collection("testHeartsRemoteWalletBatches").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task Paid_advance_freezes_queue_expiry_notice_and_a_corrupt_pending_plan_does_not_starve_deadlines()
    {
        var (db, store, _) = Create(); await Seed(db, "alice");
        var queue = await store.JoinAsync("alice", "Alice", 1m, "join-expiry", Start, default);
        await db.Collection(FirestorePaidHeartsStore.PreparedCollection).Document("000-corrupt").SetAsync(new Dictionary<string, object>
        { ["status"] = "pending" });
        var expired = await store.AdvanceCommandAsync("alice", queue.Queue!.MatchId, queue.Version, "expire", Start.AddMinutes(2), default);
        Assert.Equal("idle", expired.Kind);
        Assert.Contains("refunded", expired.Notice);
        var replay = await store.AdvanceCommandAsync("alice", queue.Queue.MatchId, queue.Version, "expire", Start.AddMinutes(5), default);
        Assert.Equal(JsonSerializer.Serialize(expired, Json), JsonSerializer.Serialize(replay, Json));
        await Seed(db, "bruno");
        await store.JoinAsync("bruno", "Bruno", 1m, "funded", Start, default);
        await store.SweepAsync(Start.AddMinutes(2), default);
        Assert.Equal("idle", (await store.SessionAsync("bruno", Start.AddMinutes(2), default)).Kind);
        Assert.Equal(100m, await Credits(db, "bruno"));
        Assert.Equal("pending", (await db.Collection(FirestorePaidHeartsStore.PreparedCollection).Document("000-corrupt").GetSnapshotAsync()).GetValue<string>("status"));
    }

    [Fact]
    public async Task Atomic_decline_keeps_original_answer_after_funding_and_new_key_can_admit()
    {
        var (db, store, _) = Create();
        await Assert.ThrowsAsync<PaidHeartsInsufficientFundsException>(() => store.JoinAsync("alice", "Alice", 1m, "declined", Start, default));
        Assert.Empty((await db.Collection("heartsPaidMatches").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection(FirestorePaidHeartsStore.WalletJournalCollection).GetSnapshotAsync()).Documents);
        await Seed(db, "alice");
        await Assert.ThrowsAsync<PaidHeartsInsufficientFundsException>(() => store.JoinAsync("alice", "Alice", 1m, "declined", Start, default));
        Assert.Equal("queue", (await store.JoinAsync("alice", "Alice", 1m, "funded-new-key", Start, default)).Kind);
        Assert.Equal(99m, await Credits(db, "alice"));
    }

    [Fact]
    public async Task Lost_entry_and_refund_replies_recover_before_public_state_and_replay_while_paused()
    {
        var (db, _, roster) = Create(); await Seed(db, "alice");
        var bank = new PaidHeartsTestBank(db) { LoseNextReply = true };
        var store = PaidHeartsTestStore.Create(db, roster, bank: bank);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.JoinAsync("alice", "Alice", 1m, "lost-entry", Start, default));
        Assert.Equal(99m, await Credits(db, "alice"));
        Assert.Empty((await db.Collection("heartsPaidMatches").GetSnapshotAsync()).Documents);
        Assert.NotEmpty((await db.Collection(FirestorePaidHeartsStore.LocksCollection).GetSnapshotAsync()).Documents);
        await db.Document(FortuneForge.Games.Firestore.FirestoreManagedPlayerPolicy.StatePath).SetAsync(new Dictionary<string, object>
        { ["botsEnabled"] = false, ["gameBots"] = new Dictionary<string, object> { [HeartsBackend.GameId] = false } });
        var unrelated = db.StartBatch();
        for (var index = 0; index < 110; index++) unrelated.Create(db.Collection(FirestorePaidHeartsStore.PreparedCollection)
            .Document($"000-unrelated-{index:D3}"), new Dictionary<string, object> { ["status"] = "pending" });
        await unrelated.CommitAsync();
        var restarted = PaidHeartsTestStore.Create(db, roster, bank: bank, admits: () => false);
        var recovered = await restarted.SessionAsync("alice", Start, default);
        Assert.Equal("queue", recovered.Kind);
        var accepted = await restarted.JoinAsync("alice", "Another Name", 1m, "lost-entry", Start.AddHours(1), default);
        Assert.Equal(JsonSerializer.Serialize(recovered, Json), JsonSerializer.Serialize(accepted, Json));
        await Assert.ThrowsAsync<PaidHeartsConflictException>(() => restarted.JoinAsync("alice", "Alice", 2m, "lost-entry", Start, default));
        bank.LoseNextReply = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => restarted.CommandAsync("alice", recovered.Queue!.MatchId,
            "leave", recovered.Version, null, "lost-refund", Start, default));
        Assert.Equal(100m, await Credits(db, "alice"));
        Assert.Equal("idle", (await PaidHeartsTestStore.Create(db, roster, bank: bank).SessionAsync("alice", Start, default)).Kind);
        var refund = await restarted.CommandAsync("alice", recovered.Queue!.MatchId, "leave", recovered.Version, null, "lost-refund", Start, default);
        await Seed(db, "alice", 50_000);
        var replay = await restarted.CommandAsync("alice", recovered.Queue.MatchId, "leave", recovered.Version, null, "lost-refund", Start.AddHours(1), default);
        Assert.Equal(JsonSerializer.Serialize(refund, Json), JsonSerializer.Serialize(replay, Json));
        Assert.Equal(2, (await db.Collection("testHeartsRemoteWalletBatches").GetSnapshotAsync()).Count);
        Assert.Empty((await db.Collection(FirestorePaidHeartsStore.LocksCollection).GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Lost_final_payout_recovers_mixed_proof_without_mutable_ledger_dependencies()
    {
        var (db, _, roster) = Create(); await Seed(db, "alice");
        var bank = new PaidHeartsTestBank(db);
        var store = PaidHeartsTestStore.Create(db, roster, bank: bank);
        var queue = await store.JoinAsync("alice", "Alice", 1m, "historical-entry", Start, default);
        var entry = Assert.Single((await db.Collection(FirestorePaidHeartsStore.WalletJournalCollection).GetSnapshotAsync()).Documents);
        await db.Collection("balanceTransactions").Document(entry.Id).SetAsync(new Dictionary<string, object>
        {
            ["userId"] = "alice", ["currencyId"] = "slotsCredits", ["type"] = "hearts-entry", ["amountCents"] = -100L
        });
        await entry.Reference.DeleteAsync();
        await store.PlayHouseAsync("alice", queue.Queue!.MatchId, queue.Version, "house", Start.AddSeconds(5), default);
        await PrepareFinalCard(db, queue.Queue.MatchId, player => player.UserId == "alice" ? 50 : 80);
        var model = await Stored(db, queue.Queue.MatchId);
        var actor = model.Players.Single(value => value.Seat == model.State!.Turn);
        bank.LoseNextReply = true;
        var at = model.NextActionAtUtc!.Value;
        if (actor.IsAccountBacked)
        {
            var card = model.State!.Round.HandFor(actor.Seat).Single().Code;
            await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.CommandAsync(actor.UserId, model.Id, "card",
                model.Version, [card], "final-card", at.AddTicks(-1), default));
        }
        else await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.GetAsync("alice", model.Id, at, default));
        Assert.Equal(103m, await Credits(db, "alice"));
        Assert.Empty((await db.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        // The prior legacy funding was captured before the payout; an archive change cannot strand accepted recovery.
        await db.Collection("balanceTransactions").Document(entry.Id).DeleteAsync();
        var recovered = await PaidHeartsTestStore.Create(db, roster, bank: bank, admits: () => false).GetAsync("alice", model.Id, at, default);
        Assert.Equal("result", recovered.Kind);
        var evidence = Assert.Single((await db.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var json = evidence.GetValue<string>("payloadJson");
        using var payload = JsonDocument.Parse(json);
        var proof = payload.RootElement.GetProperty("financialProof");
        Assert.Equal(RoundAuditFinancialProof.MixedLegacyAndSignedWallet, proof.GetProperty("provenance").GetString());
        Assert.Contains(proof.GetProperty("walletOperations").EnumerateArray(), value => value.TryGetProperty("batchReceipt", out _));
        await db.Collection("balanceTransactions").Document(entry.Id).DeleteAsync();
        foreach (var document in (await db.Collection(FirestorePaidHeartsStore.WalletJournalCollection).GetSnapshotAsync()).Documents)
            await document.Reference.DeleteAsync();
        Assert.Equal(json, (await evidence.Reference.GetSnapshotAsync()).GetValue<string>("payloadJson"));
        Assert.Equal(GameSecurityEventDocuments.Hash(json), evidence.GetValue<string>("sha256"));
    }
}
