using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Server.Cards.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.Solitaire.Tests;

public sealed class CompetitiveSolitaireBackendTests
{
    internal static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [SolitaireEmulatorFact]
    public async Task LostBuyInReplyKeepsTheDealHiddenUntilAnotherReplicaRecoversTheOriginalBatch()
    {
        var db = Database(); var wallet = new SolitaireTestWallet { LoseNextResponse = true };
        var roster = new SolitaireTestRoster(); var store = Store(db, wallet, roster);
        const string key = "solitaire-lost-entry-0001";
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.JoinAsync("alice", "Alice", 4, 500, 3, key, 42, Now, default));
        Assert.Empty((await db.Collection("solitaireMatches").GetSnapshotAsync()).Documents);
        Assert.Equal(9_500, wallet.Balance("alice"));
        var action = Assert.Single((await db.Collection(FirestoreCompetitiveSolitaireStore.FinancialOperations).GetSnapshotAsync()).Documents);
        using var payload = JsonDocument.Parse(action.GetValue<string>("payloadJson"));
        Assert.All(payload.RootElement.GetProperty("writes").EnumerateArray(), write =>
            Assert.DoesNotContain("projects/", write.GetProperty("path").GetString()!));
        var replica = Store(db, wallet, roster, allowNew: false);
        Assert.IsType<SolitaireIdleSessionResponse>((await replica.GetSessionAsync("bob", Now, default)).Session);
        var recovered = await replica.JoinAsync("alice", "Changed display name", 4, 500, 3, key, 99, Now.AddMinutes(3), default);
        var match = Assert.IsType<SolitaireMatchSessionResponse>(recovered.Session);
        Assert.Equal(Now, match.StartedAtUtc);
        Assert.Equal(Now.AddMinutes(10), match.DeadlineAtUtc);
        Assert.Equal(1, wallet.SettlementCount);
        Assert.Equal(9_500, recovered.BalanceCents);
        await NoDirectWalletWrites(db);
    }

    [SolitaireEmulatorFact]
    public async Task ExactAdmissionReplyRemainsImmutableAfterLaterBoardMovesAndChangedTermsConflict()
    {
        var db = Database(); var wallet = new SolitaireTestWallet(); var roster = new SolitaireTestRoster();
        var store = Store(db, wallet, roster);
        const string key = "solitaire-exact-entry-0001";
        var initial = await store.JoinAsync("alice", "Alice", 4, 500, 3, key, 42, Now, default);
        var match = Assert.IsType<SolitaireMatchSessionResponse>(initial.Session);
        var moved = await store.CommandAsync("alice", match.MatchId,
            new SolitaireCommandRequest("draw", match.Version, null, null, null, null), "solitaire-draw-0001", Now.AddSeconds(1), default);
        Assert.Equal(1, Assert.IsType<SolitaireMatchSessionResponse>(moved.Session).Moves);
        var retry = await Store(db, wallet, roster).JoinAsync("alice", "Alice", 4, 500, 3, key, 99, Now.AddMinutes(1), default);
        Assert.Equal(JsonSerializer.Serialize(initial), JsonSerializer.Serialize(retry));
        Assert.Equal(1, wallet.SettlementCount);
        await Assert.ThrowsAsync<SolitaireConflictException>(() => store.JoinAsync("alice", "Alice", 4, 1_000, 3,
            key, 42, Now, default));
        await Assert.ThrowsAsync<SolitaireConflictException>(() => store.JoinAsync("alice", "Alice", 4, 500, 1,
            key, 42, Now, default));
    }

    [SolitaireEmulatorFact]
    public async Task ConcurrentEntriesKeepStakeDrawCohortsAndEachPlayersOwnClock()
    {
        var db = Database(); var wallet = new SolitaireTestWallet(); var roster = new SolitaireTestRoster();
        var store = Store(db, wallet, roster);
        var alice = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync("alice", "Alice", 4, 500, 3,
            "solitaire-cohort-alice-0001", 42, Now, default)).Session);
        var bob = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync("bob", "Bob", 4, 500, 3,
            "solitaire-cohort-bob-0001", 99, Now.AddSeconds(5), default)).Session);
        var otherDraw = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync("carol", "Carol", 4, 500, 1,
            "solitaire-cohort-carol-0001", 77, Now, default)).Session);
        var otherStake = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync("dave", "Dave", 4, 1_000, 3,
            "solitaire-cohort-dave-0001", 78, Now, default)).Session);
        Assert.Equal(alice.MatchId, bob.MatchId);
        Assert.NotEqual(alice.MatchId, otherDraw.MatchId);
        Assert.NotEqual(alice.MatchId, otherStake.MatchId);
        Assert.Equal(Now.AddSeconds(5), bob.StartedAtUtc);
        Assert.Equal(Now.AddMinutes(10).AddSeconds(5), bob.DeadlineAtUtc);
        var funding = await db.Collection("solitaireHouseFunding").Document(alice.MatchId).GetSnapshotAsync();
        Assert.Equal(1_000, funding.GetValue<long>("humanEntryCents"));
        Assert.Equal(0, funding.GetValue<long>("houseFundedEntryCents"));
        Assert.Equal("house-funded-bot-pool-v1", funding.GetValue<string>("financialClassification"));
        var players = (await db.Collection("solitaireMatchPlayers").WhereEqualTo("matchId", alice.MatchId).GetSnapshotAsync()).Documents;
        var seeds = players.Select(player => JsonDocument.Parse(player.GetValue<string>("gameStateJson"))
            .RootElement.GetProperty("seed").GetUInt32()).ToArray();
        Assert.All(seeds, seed => Assert.Equal(42u, seed));
        Assert.Equal(4, wallet.SettlementCount);
        await NoDirectWalletWrites(db);
    }

    [SolitaireEmulatorFact]
    public async Task LegacyQueuedReservationCanRefundOnceAndItsOriginalEntryRetryNeverChargesAgain()
    {
        var db = Database(); var wallet = new SolitaireTestWallet(); var roster = new SolitaireTestRoster();
        const string key = "solitaire-legacy-entry-0001";
        var ticket = await SeedLegacyQueue(db, "alice", key);
        wallet.Fund("alice", 9_500);
        var store = Store(db, wallet, roster, allowNew: false);
        var imported = await store.JoinAsync("alice", "Alice", 4, 500, 3, key, 42, Now, default);
        Assert.IsType<SolitaireQueueSessionResponse>(imported.Session);
        Assert.Equal(9_500, wallet.Balance("alice"));
        Assert.Equal(0, Assert.Single(wallet.Requests).Effects[0].WagerCents);
        wallet.LoseNextResponse = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.CancelAsync("alice", ticket,
            "solitaire-legacy-refund-0001", Now.AddSeconds(1), default));
        Assert.Equal(10_000, wallet.Balance("alice"));
        var recovered = await Store(db, wallet, roster, allowNew: false).GetSessionAsync("alice", Now.AddSeconds(2), default);
        Assert.IsType<SolitaireIdleSessionResponse>(recovered.Session);
        var exact = await store.CancelAsync("alice", ticket, "solitaire-legacy-refund-0001", Now.AddHours(1), default);
        Assert.IsType<SolitaireIdleSessionResponse>(exact.Session);
        Assert.Equal(10_000, exact.BalanceCents);
        Assert.Equal(2, wallet.SettlementCount);
        Assert.Equal("cancelled", (await db.Collection("solitaireTickets").Document(ticket).GetSnapshotAsync()).GetValue<string>("status"));
        await NoDirectWalletWrites(db);
    }

    [SolitaireEmulatorFact]
    public async Task NoContestKeepsTheOriginalStakeClaimableAndLostClaimReplyCannotPayTwice()
    {
        var db = Database(); var wallet = new SolitaireTestWallet(); var roster = new SolitaireTestRoster();
        var store = Store(db, wallet, roster);
        var joined = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync("alice", "Alice", 4, 500, 3,
            "solitaire-no-contest-0001", 42, Now, default)).Session);
        var finished = await store.GetSessionAsync("alice", Now.AddHours(5), default);
        var result = Assert.IsType<SolitaireResultSessionResponse>(finished.Session);
        Assert.Equal(joined.MatchId, result.MatchId);
        Assert.Equal(5m, result.WinnerPayoutCredits);
        Assert.True(result.CanClaim);
        var originalCompletion = Assert.Single((await db.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var originalPayload = originalCompletion.GetValue<string>("payloadJson");
        using (var completion = JsonDocument.Parse(originalPayload))
        {
            Assert.Equal("claimable-entitlement-only", completion.RootElement.GetProperty("payoutDisposition").GetString());
            var proof = completion.RootElement.GetProperty("financialProof");
            Assert.Equal("signed-wallet", proof.GetProperty("provenance").GetString());
            var entry = Assert.Single(proof.GetProperty("entries").EnumerateArray());
            Assert.Equal(-500, entry.GetProperty("amountCents").GetInt64());
            Assert.Equal(500, Assert.Single(proof.GetProperty("walletOperations").EnumerateArray())
                .GetProperty("batchRequest").GetProperty("effects")[0].GetProperty("wagerCents").GetInt64());
        }
        wallet.LoseNextResponse = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.ClaimAsync("alice", joined.MatchId,
            "solitaire-no-contest-claim-0001", Now.AddHours(5), default));
        Assert.Equal(10_000, wallet.Balance("alice"));
        var replica = Store(db, wallet, roster, allowNew: false);
        var recovered = await replica.GetSessionAsync("alice", Now.AddHours(6), default);
        Assert.IsType<SolitaireIdleSessionResponse>(recovered.Session);
        var retry = await replica.ClaimAsync("alice", joined.MatchId, "solitaire-no-contest-claim-0001", Now.AddHours(7), default);
        Assert.Equal(10_000, retry.BalanceCents);
        Assert.Equal(2, wallet.SettlementCount);
        var history = Assert.Single((await replica.GetHistoryPageAsync("alice", 20, null, Now.AddHours(7), default)).Items);
        Assert.Equal(SolitaireClaimStatuses.Completed, history.ClaimStatus);
        Assert.False(history.CanClaim);
        Assert.Single((await db.Collection(RoundAuditDocuments.Evidence).GetSnapshotAsync()).Documents);
        var summary = Assert.Single((await db.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        Assert.Equal(1, summary.GetValue<long>("securityAuditGeneration"));
        Assert.Equal(Now.AddHours(5), summary.GetValue<Timestamp>("securityAuditNextAttemptAtUtc").ToDateTime());
        var claimEvent = Assert.Single((await db.Collection(GameSecurityEventDocuments.CollectionName)
            .WhereEqualTo("eventKind", "payout-claimed").GetSnapshotAsync()).Documents);
        var claimPayload = claimEvent.GetValue<string>("payloadJson");
        Assert.Equal(GameSecurityEventDocuments.Hash(claimPayload), claimEvent.GetValue<string>("sha256"));
        using (var claim = JsonDocument.Parse(claimPayload))
        {
            var state = claim.RootElement.GetProperty("state");
            Assert.Equal(originalCompletion.Id, state.GetProperty("completionEvidenceId").GetString());
            Assert.Equal(originalCompletion.GetValue<string>("sha256"), state.GetProperty("completionEvidenceSha256").GetString());
            Assert.True(state.GetProperty("completionEvidenceAvailable").GetBoolean());
            var proof = state.GetProperty("financialProof");
            Assert.Equal(500, Assert.Single(proof.GetProperty("entries").EnumerateArray()).GetProperty("amountCents").GetInt64());
            Assert.Equal(10_000, Assert.Single(proof.GetProperty("walletOperations").EnumerateArray())
                .GetProperty("batchReceipt").GetProperty("accounts")[0].GetProperty("balanceAfterCents").GetInt64());
        }
        await db.Collection("cardGameResults").Document(FirestoreCompetitiveSolitaireStore.CreateLookupKey($"solitaire\n{joined.MatchId}\nalice"))
            .UpdateAsync("payoutCents", 1L);
        Assert.Equal(originalPayload, (await originalCompletion.Reference.GetSnapshotAsync()).GetValue<string>("payloadJson"));
        Assert.Equal(claimPayload, (await claimEvent.Reference.GetSnapshotAsync()).GetValue<string>("payloadJson"));
        await NoDirectWalletWrites(db);
    }

    [SolitaireEmulatorFact]
    public async Task CompletionFreezesMixedLegacyAndSignedEntryProofBeforeMutableFundingRowsChange()
    {
        var db = Database(); var wallet = new SolitaireTestWallet(); var roster = new SolitaireTestRoster();
        var store = Store(db, wallet, roster);
        const string aliceKey = "solitaire-legacy-mixed-0001";
        var joined = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync("alice", "Alice", 4, 500, 3,
            aliceKey, 42, Now, default)).Session);
        var ticketId = FirestoreCompetitiveSolitaireStore.CreateLookupKey($"alice\n{aliceKey}");
        var ticket = db.Collection("solitaireTickets").Document(ticketId);
        await ticket.UpdateAsync(new Dictionary<string, object>
        {
            ["backendOwner"] = FieldValue.Delete, ["buyInWalletOperationId"] = FieldValue.Delete,
            ["buyInWalletBatchId"] = FieldValue.Delete,
        });
        await db.Collection("balanceTransactions").Document(ticketId + "-buyin").CreateAsync(new Dictionary<string, object>
        {
            ["userId"] = "alice", ["currencyId"] = "slotsCredits", ["type"] = "solitaire-buyin", ["amount"] = -5d,
        });
        await store.JoinAsync("bob", "Bob", 4, 500, 3, "solitaire-signed-mixed-0001", 88, Now.AddSeconds(1), default);
        await store.SweepAsync(Now.AddHours(5), default);
        var evidence = await db.Collection(RoundAuditDocuments.Evidence)
            .Document(FirestoreCompetitiveSolitaireStore.CreateLookupKey($"solitaire\n{joined.MatchId}")).GetSnapshotAsync();
        var frozen = evidence.GetValue<string>("payloadJson");
        using var capture = JsonDocument.Parse(frozen);
        var proof = capture.RootElement.GetProperty("financialProof");
        Assert.Equal("mixed-legacy-and-signed-wallet", proof.GetProperty("provenance").GetString());
        Assert.Equal(2, proof.GetProperty("entries").GetArrayLength());
        Assert.Single(proof.GetProperty("walletOperations").EnumerateArray());
        await ticket.UpdateAsync("buyInCents", 1L);
        await db.Collection("balanceTransactions").Document(ticketId + "-buyin").UpdateAsync("amount", -1d);
        Assert.Equal(frozen, (await evidence.Reference.GetSnapshotAsync()).GetValue<string>("payloadJson"));
        Assert.All(proof.GetProperty("entries").EnumerateArray(), entry => Assert.Equal(-500, entry.GetProperty("amountCents").GetInt64()));
    }

    [SolitaireEmulatorFact]
    public async Task LegacyClaimWithoutRetainedCompletionPreservesEntitlementAndRecordsUnavailableAnchor()
    {
        var db = Database(); var wallet = new SolitaireTestWallet(); var roster = new SolitaireTestRoster();
        var store = Store(db, wallet, roster);
        var joined = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync("alice", "Alice", 4, 500, 3,
            "solitaire-legacy-completion-0001", 42, Now, default)).Session);
        await store.SweepAsync(Now.AddHours(5), default);
        var completionId = FirestoreCompetitiveSolitaireStore.CreateLookupKey($"solitaire\n{joined.MatchId}");
        await db.Collection(RoundAuditDocuments.Evidence).Document(completionId).DeleteAsync();
        await db.Collection(RoundAuditDocuments.Summaries).Document(completionId).DeleteAsync();
        await db.Collection("cardGameResults").Document(FirestoreCompetitiveSolitaireStore.CreateLookupKey($"solitaire\n{joined.MatchId}\nalice"))
            .UpdateAsync("backendOwner", FieldValue.Delete);
        await store.ClaimAsync("alice", joined.MatchId, "solitaire-legacy-claim-0001", Now.AddHours(6), default);
        Assert.Equal(10_000, wallet.Balance("alice"));
        var claimEvent = Assert.Single((await db.Collection(GameSecurityEventDocuments.CollectionName)
            .WhereEqualTo("eventKind", "payout-claimed").GetSnapshotAsync()).Documents);
        using var claim = JsonDocument.Parse(claimEvent.GetValue<string>("payloadJson"));
        var state = claim.RootElement.GetProperty("state");
        Assert.Equal(completionId, state.GetProperty("completionEvidenceId").GetString());
        Assert.False(state.GetProperty("completionEvidenceAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("completionEvidenceSha256").ValueKind);
        Assert.Equal(500, Assert.Single(state.GetProperty("financialProof").GetProperty("entries").EnumerateArray())
            .GetProperty("amountCents").GetInt64());
    }

    [SolitaireEmulatorFact]
    public async Task ChangedLiveEntitlementCannotCreateAClaimAboveTheImmutableCompletion()
    {
        var db = Database(); var wallet = new SolitaireTestWallet(); var roster = new SolitaireTestRoster();
        var store = Store(db, wallet, roster);
        var joined = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync("alice", "Alice", 4, 500, 3,
            "solitaire-original-entitlement-0001", 42, Now, default)).Session);
        await store.SweepAsync(Now.AddHours(5), default);
        await db.Collection("solitaireMatchPlayers").Document(FirestoreCompetitiveSolitaireStore.CreateLookupKey($"{joined.MatchId}\nalice"))
            .UpdateAsync("payoutCents", 5_000L);
        await db.Collection("cardGameResults").Document(FirestoreCompetitiveSolitaireStore.CreateLookupKey($"solitaire\n{joined.MatchId}\nalice"))
            .UpdateAsync("payoutCents", 5_000L);
        await Assert.ThrowsAsync<SolitaireConflictException>(() => store.ClaimAsync("alice", joined.MatchId,
            "solitaire-tampered-claim-0001", Now.AddHours(6), default));
        await db.Collection("solitaireMatchPlayers").Document(FirestoreCompetitiveSolitaireStore.CreateLookupKey($"{joined.MatchId}\nalice"))
            .UpdateAsync("payoutCents", 500L);
        await db.Collection("cardGameResults").Document(FirestoreCompetitiveSolitaireStore.CreateLookupKey($"solitaire\n{joined.MatchId}\nalice"))
            .UpdateAsync("payoutCents", 500L);
        await db.Collection(RoundAuditDocuments.Evidence).Document(FirestoreCompetitiveSolitaireStore.CreateLookupKey($"solitaire\n{joined.MatchId}"))
            .DeleteAsync();
        await Assert.ThrowsAsync<SolitaireConflictException>(() => store.ClaimAsync("alice", joined.MatchId,
            "solitaire-unverifiable-claim-0001", Now.AddHours(6), default));
        Assert.Equal(9_500, wallet.Balance("alice"));
        Assert.Equal(1, wallet.SettlementCount);
    }

    [SolitaireEmulatorFact]
    public async Task UnknownWalletOutcomeBlocksOtherCohortMutationsUntilTheOriginalBatchIsRecovered()
    {
        var db = Database(); var wallet = new SolitaireTestWallet(); var roster = new SolitaireTestRoster();
        var store = Store(db, wallet, roster);
        var bob = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync("bob", "Bob", 6, 1_000, 1,
            "solitaire-gate-bob-0001", 71, Now, default)).Session);
        wallet.Unavailable = true;
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.JoinAsync("alice", "Alice", 4, 500, 3,
            "solitaire-gate-alice-0001", 42, Now, default));
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => store.CommandAsync("bob", bob.MatchId,
            new SolitaireCommandRequest("draw", bob.Version, null, null, null, null), "solitaire-gate-draw-0001", Now.AddSeconds(1), default));
        wallet.Unavailable = false;
        var advanced = await Store(db, wallet, roster).CommandAsync("bob", bob.MatchId,
            new SolitaireCommandRequest("draw", bob.Version, null, null, null, null), "solitaire-gate-draw-0001", Now.AddSeconds(1), default);
        Assert.Equal(1, Assert.IsType<SolitaireMatchSessionResponse>(advanced.Session).Moves);
        Assert.Equal(2, wallet.SettlementCount);
        Assert.Equal(9_500, wallet.Balance("alice"));
    }

    [SolitaireEmulatorFact]
    public async Task PublishedStateWithoutItsReplyCacheRecoversWithoutRepeatingWalletOrAdmission()
    {
        var db = Database(); var wallet = new SolitaireTestWallet(); var roster = new SolitaireTestRoster();
        var store = Store(db, wallet, roster);
        const string key = "solitaire-published-crash-0001";
        var initial = await store.JoinAsync("alice", "Alice", 4, 500, 3, key, 42, Now, default);
        var id = FirestoreCompetitiveSolitaireStore.CreateLookupKey($"alice\n{key}");
        await db.Collection(FirestoreCompetitiveSolitaireStore.FinancialReplies).Document(id).DeleteAsync();
        await db.Collection(FirestoreCompetitiveSolitaireStore.FinancialOperations).Document(id).UpdateAsync("stage", "published");
        await db.Collection("solitaireWalletShards").Document("legacy-cohorts-v1").SetAsync(new { operationId = id, userId = "alice" });
        var restored = await Store(db, wallet, roster).JoinAsync("alice", "Alice", 4, 500, 3, key, 99, Now.AddMinutes(5), default);
        Assert.Equal(JsonSerializer.Serialize(initial), JsonSerializer.Serialize(restored));
        Assert.Equal(1, wallet.SettlementCount);
        Assert.Single((await db.Collection("solitaireTickets").GetSnapshotAsync()).Documents);
    }

    [SolitaireEmulatorFact]
    public async Task ManagedHourlySeatsStayHouseFundedAndRetainOriginalProfileLeaseSemantics()
    {
        var db = Database(); var wallet = new SolitaireTestWallet(); var roster = new SolitaireTestRoster();
        var store = Store(db, wallet, roster, managed: true);
        var joined = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync("alice", "Alice", 4, 500, 3,
            "solitaire-managed-human-0001", 42, Now, default)).Session);
        await store.SweepAsync(Now.AddHours(1), default);
        var players = (await db.Collection("solitaireMatchPlayers").WhereEqualTo("matchId", joined.MatchId).GetSnapshotAsync()).Documents;
        Assert.Equal(2, players.Count);
        Assert.Single(players, player => player.GetValue<bool>("isSynthetic"));
        var funding = await db.Collection("solitaireHouseFunding").Document(joined.MatchId).GetSnapshotAsync();
        Assert.Equal(500, funding.GetValue<long>("humanEntryCents"));
        Assert.Equal(500, funding.GetValue<long>("houseFundedEntryCents"));
        Assert.Equal(1, wallet.SettlementCount);
        Assert.DoesNotContain(wallet.ReadAccounts, account => account.StartsWith("managed-", StringComparison.Ordinal));
        roster.Unavailable = true;
        await Assert.ThrowsAsync<ManagedPlayerRosterUnavailableException>(() => store.GetSessionAsync("alice", Now.AddHours(1).AddSeconds(1), default));
        var managed = Assert.Single((await db.Collection("solitaireMatchPlayers").WhereEqualTo("matchId", joined.MatchId).GetSnapshotAsync()).Documents,
            player => player.GetValue<bool>("isSynthetic"));
        Assert.NotEqual(SolitairePlayerStatuses.Forfeited, managed.GetValue<string>("status"));
        roster.Unavailable = false;
        roster.OwnsProfiles = false;
        _ = await store.GetSessionAsync("alice", Now.AddHours(1).AddSeconds(2), default);
        managed = await managed.Reference.GetSnapshotAsync();
        Assert.NotEqual(SolitairePlayerStatuses.Playing, managed.GetValue<string>("status"));
        Assert.Equal(1, wallet.SettlementCount);
    }

    [SolitaireEmulatorFact]
    public async Task ConcurrentRetriesPublishOneAuthoritativeDealAndOneBuyIn()
    {
        var db = Database(); var wallet = new SolitaireTestWallet(); var roster = new SolitaireTestRoster();
        const string key = "solitaire-parallel-entry-0001";
        var results = await Task.WhenAll(
            Store(db, wallet, roster).JoinAsync("alice", "Alice", 4, 500, 3, key, 42, Now, default),
            Store(db, wallet, roster).JoinAsync("alice", "Alice", 4, 500, 3, key, 99, Now.AddSeconds(1), default));
        Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(results[1]));
        Assert.Equal(1, wallet.SettlementCount);
        Assert.Single((await db.Collection("solitaireMatches").GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection("solitaireTickets").GetSnapshotAsync()).Documents);
    }

    internal static FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-solitaire-paid-{Guid.NewGuid():N}", EmulatorDetection = EmulatorDetection.EmulatorOnly,
    }.Build();

    internal static FirestoreCompetitiveSolitaireStore Store(FirestoreDb db, SolitaireTestWallet wallet,
        SolitaireTestRoster roster, bool allowNew = true, bool managed = false) =>
        new(db, wallet, wallet, roster, new CompetitiveSolitaireOptions { AllowNewPaidRounds = allowNew, AllowSingleHumanBotFill = managed });

    internal static async Task<string> SeedLegacyQueue(FirestoreDb db, string user, string key)
    {
        var id = FirestoreCompetitiveSolitaireStore.CreateLookupKey($"{user}\n{key}");
        const string partition = "players-4-buyin-500-draw-3";
        await db.Collection("solitaireTickets").Document(id).SetAsync(new Dictionary<string, object>
        {
            ["ticketId"] = id, ["userId"] = user, ["displayName"] = "Alice", ["playerCount"] = 4L,
            ["buyInCents"] = 500L, ["partitionKey"] = partition, ["status"] = "queued",
            ["joinedAt"] = Timestamp.FromDateTime(Now), ["matchId"] = "", ["drawCount"] = 3L,
            ["schemaVersion"] = 2L, ["dealSeed"] = 42L,
        });
        await db.Collection("solitairePlayerSessions").Document(FirestoreCompetitiveSolitaireStore.CreateLookupKey(user))
            .SetAsync(new { userId = user, kind = "queued", ticketId = id, matchId = "" });
        await db.Collection("solitaireQueuePartitions").Document(partition).SetAsync(new Dictionary<string, object>
        {
            ["partitionKey"] = partition, ["playerCount"] = 4L, ["buyInCents"] = 500L, ["drawCount"] = 3L,
            ["ticketIds"] = new[] { id }, ["hasQueuedPlayers"] = true,
        });
        await db.Collection("solitaireCommandGuards").Document(FirestoreCompetitiveSolitaireStore.CreateLookupKey($"{user}\n{key}"))
            .SetAsync(new { userId = user, operation = "join", targetId = id, detail = "4:500:3", createdAt = Timestamp.FromDateTime(Now) });
        return id;
    }

    private static async Task NoDirectWalletWrites(FirestoreDb db)
    {
        Assert.Empty((await db.Collection("userBalances").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }
}

public sealed class SolitaireEmulatorFactAttribute : FactAttribute
{
    public SolitaireEmulatorFactAttribute()
    {
        var host = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
        if (string.IsNullOrWhiteSpace(host)) Skip = "Run against the Firestore emulator to verify Competitive Solitaire ownership.";
        else if (!host.StartsWith("127.0.0.1:", StringComparison.OrdinalIgnoreCase) && !host.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Competitive Solitaire tests require a localhost emulator.");
    }
}
