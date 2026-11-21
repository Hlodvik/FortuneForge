using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Blackjack;
using FortuneForge.Server.Cards.Blackjack;
using FortuneForge.Server.Cards.Blackjack.Table;
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Accounts;
using FortuneForge.Server.Tests.Blackjack;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

[Collection("Blackjack table Firestore emulator")]
public sealed class BlackjackWalletFinancialAuditTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private const string Player = "alice";
    private const string Opening = "blackjack-audit-opening";
    private const string Hit = "blackjack-audit-hit";
    private const string Stand = "blackjack-audit-stand";
    private static readonly DateTime Now = new(2026, 11, 21, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Signed_hand_reconciles_each_command_and_rejects_a_missing_no_op_receipt_or_tampered_credit()
    {
        var database = Database();
        await BalanceAsync(database, 1_000);
        var store = Store(database);
        var opening = (await store.StartAsync(Player, Opening, 100, Deck(), Now.AddTicks(7), default)).Game;
        var source = database.Collection(BlackjackBackend.SoloGames).Document(opening.GameId);
        Assert.Equal(Now, (await source.GetSnapshotAsync()).GetValue<Timestamp>("createdAt").ToDateTime());
        var active = (await store.ActAsync(Player, opening.GameId, Hit, opening.Version, BlackjackActions.Hit, Now.AddSeconds(1), default)).Game;
        var completed = (await store.ActAsync(Player, opening.GameId, Stand, active.Version, BlackjackActions.Stand, Now.AddSeconds(2), default)).Game;
        Assert.Equal(200L, completed.PayoutCents);
        var (summary, payload) = await SavedAsync(database, completed.GameId);
        await source.UpdateAsync("walletCommandIds", Array.Empty<string>());
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));

        var hitId = GameWalletSettlementIdentity.CreateId("blackjack", Player, BlackjackSoloStore.Hash($"{Player}\naction\n{Hit}"));
        var noOp = database.Collection("walletRoundSettlements").Document(hitId);
        var original = (await noOp.GetSnapshotAsync()).ToDictionary();
        await noOp.DeleteAsync();
        Assert.Contains(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default),
            finding => finding.Code == "blackjack-wallet-evidence-invalid");
        await noOp.CreateAsync(original);

        var payoutId = GameWalletSettlementIdentity.CreateId("blackjack", Player, BlackjackSoloStore.Hash($"{Player}\naction\n{Stand}"));
        await database.Collection("balanceTransactions").Document(payoutId + "-payout").UpdateAsync("amountCents", 201L);
        Assert.Contains(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default),
            finding => finding.Code == "blackjack-wallet-evidence-invalid");
        Assert.Equal(1_100L, await FortuneForge.Server.Accounts.Storage.FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, Player, false, default));
    }

    [Fact]
    public async Task MissingOriginalWagerRemainsAnExplicitCoverageWarning()
    {
        var database = Database();
        await BalanceAsync(database, 900);
        var id = BlackjackSoloStore.Hash($"{Player}\n{Opening}");
        var active = BlackjackRules.Deal(id, Player, 100, Deck(), Now);
        await database.Collection(BlackjackBackend.SoloGames).Document(id)
            .CreateAsync(BlackjackSoloCodec.Create(active, Opening));
        await Store(database).ActAsync(Player, id, Stand, active.Version, BlackjackActions.Stand, Now.AddSeconds(1), default);
        var (summary, payload) = await SavedAsync(database, id);
        Assert.Equal("incomplete", summary.AuditStatus);
        var finding = Assert.Single(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));
        Assert.Equal(RoundAuditFinancialProof.UnavailableLegacyEvidenceCode, finding.Code);
        Assert.Equal("warning", finding.Severity);
    }

    [Theory]
    [InlineData(false, "blackjack-table-double")]
    [InlineData(true, "blackjack-table-insurance")]
    public async Task Table_reconciles_exact_wallet_and_action_ledger_records(bool insurance, string changedKind)
    {
        var database = Database();
        await BalanceAsync(database, 10_000);
        var prefix = insurance ? new[] { "9|spades", "A|hearts", "7|diamonds", "K|spades" } :
            new[] { "8|spades", "6|hearts", "8|diamonds", "10|spades", "3|diamonds", "2|hearts", "10|clubs", "9|clubs" };
        var store = BlackjackPlatformTests.Table(database, () => DeckWithPrefix(prefix), () => 1931UL, multiplayerEnabled: false);
        var joined = Assert.IsType<BlackjackTablePlaySessionResponse>(
            (await store.JoinAsync(Player, "Alice", 0, "table-join-command", Now, default)).Session);
        var tableId = joined.Table.TableId;
        await store.WagerAsync(Player, tableId, 100, joined.Version, "table-wager-command", Now.AddSeconds(1), default);
        var active = await WaitForTableAsync(store, Now.AddSeconds(2), session => session.Table.Transition is null &&
            session.Table.Phase == (insurance ? BlackjackTablePhases.Insurance : BlackjackTablePhases.Active));
        await store.ActionAsync(Player, tableId, insurance ? "insurance" : "split", active.Version,
            "table-first-action", Now.AddMinutes(1), default);
        if (!insurance)
        {
            for (var hand = 0; hand < 2; hand++)
            {
                var actionTime = Now.AddMinutes(1).AddSeconds(hand + 1);
                var next = await WaitForTableAsync(store, actionTime, session =>
                    session.Table.Transition is null && session.Table.LegalActions.Contains("double"));
                await store.ActionAsync(Player, tableId, "double", next.Version, "table-double-" + hand, actionTime, default);
            }
        }
        await WaitForTableAsync(store, Now.AddMinutes(2), session => session.Table.Phase == "settlement" && session.Table.Transition is null);
        var audit = Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        var (summary, payload) = await SavedAsync(database, audit.GetValue<string>("roundId"));
        await database.Collection(BlackjackBackend.Tables).Document(tableId).UpdateAsync("walletCommandIds", Array.Empty<string>());
        Assert.Empty(TableOutcomeReplayAudit.Inspect(summary, payload));
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));

        var records = await database.Collection(BlackjackBackend.TableLedgerEvidence).WhereEqualTo("type", changedKind).GetSnapshotAsync();
        var record = records.Documents.First();
        await record.Reference.UpdateAsync("walletSettlementId", "different-settlement");
        Assert.Contains(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default),
            finding => finding.Code == "blackjack-ledger-mismatch");
    }

    private static async Task<BlackjackTablePlaySessionResponse> WaitForTableAsync(FirestoreBlackjackTableStore store,
        DateTime start, Func<BlackjackTablePlaySessionResponse, bool> done)
    {
        for (var step = 0; step < 80; step++)
        {
            var session = Assert.IsType<BlackjackTablePlaySessionResponse>(
                (await store.GetSessionAsync(Player, start.AddMilliseconds(step * 500), default)).Session);
            if (done(session)) return session;
        }
        throw new InvalidOperationException("The Blackjack table did not reach the expected phase.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_debit_is_reconciled_with_original_or_worker_terminal_settlement(bool nativeTerminal)
    {
        var database = Database();
        var id = BlackjackSoloStore.Hash($"{Player}\n{Opening}");
        var started = BlackjackRules.Deal(id, Player, 100, Deck(), Now);
        var active = BlackjackRules.ApplyAction(started, BlackjackActions.Hit, Now.AddSeconds(1));
        var originalWager = await LegacyLedgerAsync(database, id + "-wager", "blackjack-wager", Opening, -100, 900, Now);
        var final = BlackjackRules.ApplyAction(active, BlackjackActions.Stand, Now.AddSeconds(2));
        Assert.Equal(200L, final.PayoutCents);
        var source = database.Collection(BlackjackBackend.SoloGames).Document(id);
        RoundAuditSummary summary;
        string payload;
        if (nativeTerminal)
        {
            await BalanceAsync(database, 900);
            await source.CreateAsync(BlackjackSoloCodec.Create(active, Opening));
            await Store(database).ActAsync(Player, id, Stand, active.Version, BlackjackActions.Stand, Now.AddSeconds(2), default);
            (summary, payload) = await SavedAsync(database, id);
        }
        else
        {
            await source.CreateAsync(BlackjackSoloCodec.Create(final, Opening));
            await LegacyLedgerAsync(database, id + "-payout", "blackjack-payout", Stand + "-payout", 200, 1_100, final.UpdatedAtUtc);
            var capture = SinglePlayerRoundRecord.Create("blackjack", id, Player, 100, 200, Now, final.UpdatedAtUtc,
                BlackjackBackend.SoloGames, id, new { game = final, terminalAction = BlackjackActions.Stand },
                "legacy-blackjack-cards-and-final-action-v1").Audit;
            (summary, payload) = (capture.Summary, capture.PayloadJson);
        }
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));
        await originalWager.UpdateAsync("amount", -0.99d);
        Assert.Contains(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default),
            finding => finding.Code == "blackjack-ledger-mismatch");
    }

    private static BlackjackSoloStore Store(FirestoreDb database)
    {
        var wallet = new GameWalletFirestoreTestClient(database, "blackjack");
        return new BlackjackSoloStore(database, wallet, wallet);
    }

    private static string[] Deck()
    {
        string[] prefix = ["9|clubs", "7|clubs", "5|hearts", "9|diamonds", "6|spades", "K|hearts"];
        return DeckWithPrefix(prefix);
    }

    private static string[] DeckWithPrefix(string[] prefix) => prefix.Concat(BlackjackRules.CreateShuffledDeck()
        .Where(card => !prefix.Contains(card)).Order(StringComparer.Ordinal)).ToArray();

    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-blackjack-financial-{Guid.NewGuid():N}", Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None,
    }.Build();

    private static Task BalanceAsync(FirestoreDb database, long cents) => database.Collection("userBalances")
        .Document(Player + "_slotsCredits").CreateAsync(new Dictionary<string, object>
        {
            ["userId"] = Player, ["currencyId"] = "slotsCredits", ["available"] = cents / 100,
            ["availableFractionalCents"] = cents % 100, ["reserved"] = 0L, ["version"] = 1L,
        });

    private static async Task<DocumentReference> LegacyLedgerAsync(FirestoreDb database, string id, string type, string key,
        long amount, long after, DateTime occurredAt)
    {
        var ledger = database.Collection("balanceTransactions").Document(id);
        await ledger.CreateAsync(new Dictionary<string, object>
        {
            ["transactionId"] = id, ["userId"] = Player, ["currencyId"] = "slotsCredits", ["type"] = type,
            ["amount"] = amount / 100d, ["balanceAfter"] = after / 100d, ["idempotencyKey"] = key,
            ["createdAt"] = Timestamp.FromDateTime(occurredAt),
        });
        return ledger;
    }

    private static async Task<(RoundAuditSummary Summary, string Payload)> SavedAsync(FirestoreDb database, string round)
    {
        var id = GameSecurityEventDocuments.Hash($"blackjack\n{round}");
        var saved = await database.Collection(RoundAuditDocuments.Summaries).Document(id).GetSnapshotAsync();
        var evidence = await database.Collection(RoundAuditDocuments.Evidence).Document(id).GetSnapshotAsync();
        return (FirestoreGameRoundAuditStore.Summary(saved), evidence.GetValue<string>("payloadJson"));
    }
}
