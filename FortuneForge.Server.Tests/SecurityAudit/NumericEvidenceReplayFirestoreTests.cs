using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Keno;
using FortuneForge.Games.SicBo;
using FortuneForge.Server.Dice.SicBo;
using FortuneForge.Server.Numbers.Keno;
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
        var db = Database(); await Seed(db);
        var round = (await new KenoFirestoreStore(db).StartAsync("alice", Guid.NewGuid().ToString("N"),
            new KenoTicket([1]), 100, new KenoDraw(Enumerable.Range(1, 20)), DateTimeOffset.UtcNow, default)).Round;
        var summary = await Summary(db, "keno", round.RoundId);
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
        var db = Database(); await Seed(db);
        var round = (await new SicBoFirestoreStore(db).StartAsync("alice", Guid.NewGuid().ToString("N"),
            [new SicBoStoredBet("small", 100, null, null, null, null)], new SicBoRoll(1, 2, 3),
            DateTimeOffset.UtcNow, default)).Round;
        var summary = await Summary(db, "sic-bo", round.RoundId);
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

    private static Task Seed(FirestoreDb db) => db.Collection("userBalances")
        .Document("alice_slotsCredits").SetAsync(new Dictionary<string, object>
        { ["available"] = 100L, ["availableFractionalCents"] = 0L });

    private static async Task<RoundAuditSummary> Summary(FirestoreDb db, string gameId, string roundId) =>
        FirestoreGameRoundAuditStore.Summary(await db.Collection(RoundAuditDocuments.Summaries)
            .Document(GameSecurityEventDocuments.Hash($"{gameId}\n{roundId}")).GetSnapshotAsync());
}
