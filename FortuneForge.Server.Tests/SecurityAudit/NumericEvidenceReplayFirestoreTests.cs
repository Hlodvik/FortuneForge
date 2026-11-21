using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Keno;
using FortuneForge.Games.Keno.Server;
using FortuneForge.Games.SicBo;
using FortuneForge.Games.SicBo.Server;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

[Collection("Blackjack table Firestore emulator")]
public sealed class NumericEvidenceReplayFirestoreTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    [Fact]
    public async Task Keno_financial_pass_detects_changed_source_settlement()
    {
        var db = Database();
        const string key = "legacy-keno-audit-0001";
        var round = new KenoStoreRound(GameSecurityEventDocuments.Hash($"alice\n{key}"), "alice", new KenoTicket([1]),
            new KenoDraw(Enumerable.Range(1, 20)), 1, 100, 250, "fortuneforge-keno-v3");
        var summary = await SeedHistoricalAsync(db, "keno", "kenoRounds", "kenoRoundEvents", key, round.RoundId,
            round.WagerCents, round.PayoutCents, round, "keno-ticket-and-draw-v1", KenoOutcomeAudit.Version, new()
            {
                ["ticket"] = round.Ticket.Numbers.ToArray(), ["draw"] = round.Draw.Numbers.ToArray(), ["hitCount"] = 1L,
                ["wagerCents"] = round.WagerCents, ["payoutCents"] = round.PayoutCents,
                ["paytableId"] = round.PaytableId, ["schemaVersion"] = 2L,
            });
        Assert.Empty(await NumericEvidenceReplayAudit.InspectFinancialAsync(db, summary, default));

        await db.Collection("kenoRounds").Document(round.RoundId).UpdateAsync("payoutCents", round.PayoutCents + 1);
        Assert.Contains(await NumericEvidenceReplayAudit.InspectFinancialAsync(db, summary, default),
            finding => finding.Code == "numeric-source-mismatch");
        await db.Collection("kenoRounds").Document(round.RoundId).UpdateAsync("payoutCents", round.PayoutCents);
        await db.Collection("balanceTransactions").Document($"keno-{round.RoundId}-bonus")
            .SetAsync(new Dictionary<string, object> { ["amount"] = 1d });
        Assert.Contains(await NumericEvidenceReplayAudit.InspectFinancialAsync(db, summary, default),
            finding => finding.Code == "numeric-ledger-extra");
    }

    [Fact]
    public async Task Sic_bo_financial_pass_detects_changed_wallet_debit()
    {
        var db = Database();
        const string key = "legacy-sic-bo-audit-0001";
        var round = new SicBoStoreRound(GameSecurityEventDocuments.Hash($"alice\n{key}"), "alice",
            [new SicBoStoredBet("small", 100, null, null, null, null)], new SicBoRoll(1, 2, 3));
        var summary = await SeedHistoricalAsync(db, "sic-bo", "sicBoRounds", "sicBoRoundEvents", key, round.RoundId,
            100, 200, round, "sic-bo-bets-and-dice-v1", SicBoOutcomeAudit.Version, new()
            {
                ["betsJson"] = JsonSerializer.Serialize(round.Bets), ["dice"] = round.Roll.Values.ToArray(),
                ["totalStakeCents"] = 100L, ["totalReturnCents"] = 200L, ["schemaVersion"] = 1L,
            });
        Assert.Empty(await NumericEvidenceReplayAudit.InspectFinancialAsync(db, summary, default));

        await db.Collection("balanceTransactions").Document($"sic-bo-{round.RoundId}-wager")
            .UpdateAsync("amount", -1.01d);
        Assert.Contains(await NumericEvidenceReplayAudit.InspectFinancialAsync(db, summary, default),
            finding => finding.Code == "numeric-ledger-mismatch");
    }

    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-numeric-evidence-" + Guid.NewGuid().ToString("N"),
        Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
        EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
    }.Build();

    private static async Task<RoundAuditSummary> SeedHistoricalAsync(FirestoreDb db, string game, string rounds, string events,
        string key, string roundId, long wager, long payout, object round, string kind, string auditor, Dictionary<string, object> source)
    {
        const string user = "alice";
        var now = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        source["roundId"] = roundId;
        source["userId"] = user;
        source["startIdempotencyKey"] = key;
        source["createdAt"] = Timestamp.FromDateTime(now);
        var record = SinglePlayerRoundRecord.Create(game, roundId, user, wager, payout, now, now, rounds, roundId, round, kind, true, auditor);
        var audit = RoundAuditDocuments.Create(record.Audit);
        var summary = db.Collection(RoundAuditDocuments.Summaries).Document(record.Audit.Summary.Id);
        var batch = db.StartBatch();
        batch.Set(db.Collection(rounds).Document(roundId), source);
        batch.Set(db.Collection(events).Document(roundId), new Dictionary<string, object>
        {
            ["roundId"] = roundId, ["userId"] = user, ["event"] = "settled", ["idempotencyKey"] = key,
            ["createdAt"] = Timestamp.FromDateTime(now), ["schemaVersion"] = source["schemaVersion"],
        });
        batch.Set(summary, audit.Summary);
        batch.Set(db.Collection(RoundAuditDocuments.Evidence).Document(summary.Id), audit.Evidence);
        var afterWager = 10_000 - wager;
        foreach (var (type, amount, balance, operation) in new[] { ("wager", -wager, afterWager, key), ("payout", payout, afterWager + payout, key + "-payout") })
        {
            var id = $"{game}-{roundId}-{type}";
            batch.Set(db.Collection("balanceTransactions").Document(id), new Dictionary<string, object>
            {
                ["transactionId"] = id, ["userId"] = user, ["currencyId"] = "slotsCredits", ["amount"] = amount / 100d,
                ["balanceAfter"] = balance / 100d, ["type"] = $"{game}-{type}", ["idempotencyKey"] = operation,
                ["createdAt"] = Timestamp.FromDateTime(now),
            });
        }
        await batch.CommitAsync();
        return FirestoreGameRoundAuditStore.Summary(await summary.GetSnapshotAsync());
    }
}
