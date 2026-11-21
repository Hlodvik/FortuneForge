using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Solitaire;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Cards.Solitaire;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

[Collection("Blackjack table Firestore emulator")]
public sealed class SolitaireFinancialAuditFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly DateTime Start = new(2026, 10, 26, 18, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unclaimed_award_is_coverage_warning_then_later_claim_requeues_and_reconciles(bool legacyBuyin)
    {
        var (db, store) = CreateStore();
        await SeedBalanceAsync(db);
        var started = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
            "alice", "Alice", 4, 500, 3, "join-one", 42, Start, default)).Session);
        await store.CommandAsync("alice", started.MatchId,
            new SolitaireCommandRequest(SolitaireCommandTypes.Submit, started.Version, null, null, null, null),
            "submit-one", Start.AddMinutes(1), default);
        await store.SweepAsync(Start.AddHours(4), default);

        if (legacyBuyin)
            await RestoreLegacyBuyinAsync(db, started.MatchId);

        var (summary, payload) = await ReadAuditAsync(db, started.MatchId);
        var first = await SolitaireFinancialAudit.InspectAsync(db, summary, payload, default);
        Assert.DoesNotContain(first, finding => finding.Severity == "error");
        Assert.Contains(first, finding => finding.Code == "solitaire-claim-pending" && finding.Severity == "warning");

        var auditRef = db.Collection(RoundAuditDocuments.Summaries).Document(summary.Id);
        await auditRef.UpdateAsync("securityAuditPending", false);
        var claimAt = Start.AddHours(4).AddMinutes(1);
        await store.ClaimAsync("alice", started.MatchId, "claim-one", claimAt, default);
        var queued = await auditRef.GetSnapshotAsync();
        Assert.True(queued.GetValue<bool>("securityAuditPending"));
        Assert.Equal(1, queued.GetValue<long>("securityAuditGeneration"));
        Assert.Equal(claimAt, queued.GetValue<Timestamp>("securityAuditNextAttemptAtUtc").ToDateTime());
        Assert.Empty(await SolitaireFinancialAudit.InspectAsync(db, summary, payload, default));

        var claimBatch = BatchId(FirestoreCompetitiveSolitaireStore.CreateLookupKey("alice\nclaim-one"));
        var claimId = GameWalletBatchIdentity.AccountLedgerPrefix(claimBatch, "alice") + "-payout";
        var receipt = db.Collection(GameWalletBatchFirestoreStore.ReceiptCollection).Document(claimBatch);
        var leg = db.Collection("balanceTransactions").Document(claimId);
        var originalReceipt = (await receipt.GetSnapshotAsync()).ToDictionary();
        var originalLeg = (await leg.GetSnapshotAsync()).ToDictionary();
        var balance = await FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(db, "alice", false, default);
        await receipt.DeleteAsync();
        await leg.DeleteAsync();
        Assert.Contains(await SolitaireFinancialAudit.InspectAsync(db, summary, payload, default),
            finding => finding.Code == "solitaire-financial-claim-ledger-mismatch" && finding.Severity == "error");
        Assert.Equal(balance, await FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(db, "alice", false, default));
        Assert.False((await receipt.GetSnapshotAsync()).Exists);
        Assert.False((await leg.GetSnapshotAsync()).Exists);
        await receipt.CreateAsync(originalReceipt);
        await leg.CreateAsync(originalLeg);
        await leg.UpdateAsync("amount", 4d);
        Assert.Contains(await SolitaireFinancialAudit.InspectAsync(db, summary, payload, default),
            finding => finding.Code == "solitaire-financial-claim-ledger-mismatch" && finding.Severity == "error");
    }

    private static async Task RestoreLegacyBuyinAsync(FirestoreDb db, string matchId)
    {
        var match = await db.Collection("solitaireMatches").Document(matchId).GetSnapshotAsync();
        var ticketId = Assert.Single(match.GetValue<List<string>>("ticketIds"), id => id.Length > 0);
        var batchId = BatchId(ticketId);
        var ledgers = db.Collection("balanceTransactions");
        var batchLeg = ledgers.Document(GameWalletBatchIdentity.AccountLedgerPrefix(batchId, "alice") + "-wager");
        var balanceAfter = (await batchLeg.GetSnapshotAsync()).GetValue<double>("balanceAfter");
        var oldLeg = ledgers.Document(ticketId + "-buyin");
        await oldLeg.CreateAsync(new Dictionary<string, object>
        {
            ["transactionId"] = oldLeg.Id, ["userId"] = "alice", ["currencyId"] = "slotsCredits",
            ["type"] = "solitaire-buyin", ["amount"] = -5d, ["balanceAfter"] = balanceAfter,
            ["idempotencyKey"] = "join-one", ["createdAt"] = Timestamp.FromDateTime(Start),
        });
        await db.Collection("solitaireCommandGuards").Document(ticketId).UpdateAsync(new Dictionary<string, object>
        {
            ["backendOwner"] = FieldValue.Delete, ["walletBatchId"] = FieldValue.Delete,
            ["walletOperationId"] = FieldValue.Delete,
        });
        await db.Collection(FirestoreCompetitiveSolitaireStore.FinancialOperations).Document(ticketId).DeleteAsync();
        await db.Collection(GameWalletBatchFirestoreStore.ReceiptCollection).Document(batchId).DeleteAsync();
        await batchLeg.DeleteAsync();
    }

    [Fact]
    public async Task Tampered_buyin_funding_revenue_ticket_and_result_are_independently_detected()
    {
        var (db, store) = CreateStore();
        await SeedBalanceAsync(db);
        var started = Assert.IsType<SolitaireMatchSessionResponse>((await store.JoinAsync(
            "alice", "Alice", 4, 500, 3, "join-two", 43, Start, default)).Session);
        await store.CommandAsync("alice", started.MatchId,
            new SolitaireCommandRequest(SolitaireCommandTypes.Submit, started.Version, null, null, null, null),
            "submit-two", Start.AddMinutes(1), default);
        await store.SweepAsync(Start.AddHours(4), default);
        var (summary, payload) = await ReadAuditAsync(db, started.MatchId);
        Assert.DoesNotContain(await SolitaireFinancialAudit.InspectAsync(db, summary, payload, default),
            finding => finding.Severity == "error");

        var match = await db.Collection("solitaireMatches").Document(started.MatchId).GetSnapshotAsync();
        var ticketId = Assert.Single(match.GetValue<List<string>>("ticketIds"), id => id.Length > 0);
        var buyInId = GameWalletBatchIdentity.AccountLedgerPrefix(BatchId(ticketId), "alice") + "-wager";
        await db.Collection("balanceTransactions").Document(buyInId).UpdateAsync("amount", -4d);
        await db.Collection("solitaireHouseFunding").Document(started.MatchId).UpdateAsync("humanEntryCents", 1L);
        await db.Collection("solitaireMatchRevenue").Document(started.MatchId).UpdateAsync("houseNetCents", 1L);
        await db.Collection("solitaireTickets").Document(ticketId).UpdateAsync("userId", "someone-else");
        var resultId = FirestoreCompetitiveSolitaireStore.CreateLookupKey($"solitaire\n{started.MatchId}\nalice");
        await db.Collection("cardGameResults").Document(resultId).UpdateAsync("payoutCents", 1L);
        var findings = await SolitaireFinancialAudit.InspectAsync(db, summary, payload, default);
        foreach (var code in new[] { "solitaire-financial-buyin-ledger-mismatch", "solitaire-financial-funding-mismatch",
                     "solitaire-financial-revenue-mismatch", "solitaire-financial-ticket-mismatch",
                     "solitaire-financial-result-mismatch" })
            Assert.Contains(findings, finding => finding.Code == code && finding.Severity == "error");
    }

    private (FirestoreDb Database, FirestoreCompetitiveSolitaireStore Store) CreateStore()
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = "demo-solitaire-financial-" + Guid.NewGuid().ToString("N"),
            Endpoint = fixture.Endpoint, ChannelCredentials = ChannelCredentials.Insecure,
            EmulatorDetection = EmulatorDetection.None
        }.Build();
        var profiles = new FirestoreManagedPlayerProfileRepository(database);
        var queuer = new ManagedPlayerQueuer(new ManagedPlayerProfileGenerator(profiles), profiles,
            new FirestoreManagedPlayerAssignmentStore(database));
        return (database, CompetitiveSolitaireTestStore.Create(database, queuer, TimeProvider.System, new CompetitiveSolitaireOptions
        {
            AllowSingleHumanBotFill = false, AllowNewPaidRounds = true
        }));
    }

    private static Task SeedBalanceAsync(FirestoreDb db) => db.Collection("userBalances")
        .Document("alice_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["userId"] = "alice", ["currencyId"] = "slotsCredits", ["available"] = 100L,
            ["availableFractionalCents"] = 0L, ["reserved"] = 0L,
            ["version"] = 1L, ["updatedAt"] = Timestamp.FromDateTime(Start)
        });

    private static string BatchId(string operation) => GameWalletBatchIdentity.CreateId(CompetitiveSolitaireEndpoints.GameId, operation);

    private static async Task<(RoundAuditSummary Summary, string Payload)> ReadAuditAsync(FirestoreDb db, string matchId)
    {
        var id = FirestoreCompetitiveSolitaireStore.CreateLookupKey($"solitaire\n{matchId}");
        var summary = await db.Collection(RoundAuditDocuments.Summaries).Document(id).GetSnapshotAsync();
        var evidence = await db.Collection(RoundAuditDocuments.Evidence).Document(id).GetSnapshotAsync();
        Assert.True(summary.Exists);
        Assert.True(evidence.Exists);
        return (FirestoreGameRoundAuditStore.Summary(summary), evidence.GetValue<string>("payloadJson"));
    }
}
