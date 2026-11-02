using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.AndarBahar;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.Cards;
using FortuneForge.Games.CasinoWar;
using FortuneForge.Games.DragonTiger;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.HiLo.Server;
using FortuneForge.Games.HouseHoldem;
using FortuneForge.Games.VideoPoker;
using FortuneForge.Server.Cards.Baccarat;
using FortuneForge.Server.Cards.CasinoWar;
using FortuneForge.Server.Cards.HouseHoldem;
using FortuneForge.Server.Cards.VideoPoker;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.RoundAudit;

[Collection("Blackjack table Firestore emulator")]
public sealed class PaidCardFinancialAuditTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-paid-card-financial-{Guid.NewGuid():N}", Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
        EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
    }.Build();

    [Theory]
    [InlineData("video-poker", "video-poker-wager", "video-poker-payout")]
    [InlineData("baccarat", "baccarat-wager", "baccarat-payout")]
    [InlineData("dragon-tiger", "dragon-tiger-wager", "dragon-tiger-payout")]
    [InlineData("andar-bahar", "andar-bahar-wager", "andar-bahar-payout")]
    public async Task Saved_wager_and_return_match_both_wallet_ledgers_and_tampering_is_flagged(
        string game, string wagerType, string payoutType)
    {
        var database = Database();
        const string round = "test-round";
        var capture = Capture(game, round, 100, 200, new { });
        await Ledger(database, $"{game}-{round}-wager", wagerType, -100);
        var payout = await Ledger(database, $"{game}-{round}-payout", payoutType, 200);

        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, capture.Summary, capture.PayloadJson, default));
        await payout.UpdateAsync("amount", 1.5d);
        Assert.Contains(await PaidCardFinancialAudit.InspectAsync(database, capture.Summary, capture.PayloadJson, default),
            finding => finding.Code == "paid-card-ledger-mismatch");
    }

    [Fact]
    public async Task Casino_war_reconciles_opening_wager_and_rejects_an_unearned_tie_payment()
    {
        var database = Database();
        var player = new PlayingCard(CardRank.Two, CardSuit.Clubs);
        var dealer = new PlayingCard(CardRank.Ace, CardSuit.Diamonds);
        var deck = new[] { player, dealer }
            .Concat(StandardDeck.Create().Where(card => card != player && card != dealer))
            .Take(10).ToArray();
        var round = CasinoWarRoundEngine.Start(deck, 1m);
        Assert.Equal(CasinoWarRoundPhase.Completed, round.Phase);
        Assert.Equal(0m, round.Settlement!.TotalReturn);
        var capture = Capture("casino-war", "war-test", 100, 0,
            new CasinoWarStoreRound("war-test", "alice", round, null, deck));
        await Ledger(database, "casino-war-war-test-opening-wager", "casino-war-opening-wager", -100);

        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, capture.Summary, capture.PayloadJson, default));
        await Ledger(database, "casino-war-war-test-tie-payout", "casino-war-tie-payout", 100);
        Assert.Contains(await PaidCardFinancialAudit.InspectAsync(database, capture.Summary, capture.PayloadJson, default),
            finding => finding.Code == "paid-card-ledger-mismatch");
    }

    [Fact]
    public async Task Real_HiLo_round_reconciles_operations_result_and_all_wallet_entries()
    {
        var database = Database();
        await SeedBalance(database);
        var store = new HiLoFirestoreStore(database);
        var start = await new HiLoService(store, draw: () => new(CardRank.Five, CardSuit.Clubs))
            .StartAsync("alice", new("hilo_start_000001", 100));
        var won = await new HiLoService(store, draw: () => new(CardRank.King, CardSuit.Hearts))
            .ActAsync("alice", start.Round!.RoundId, new("hilo_guess_000001", 0, "higher"));
        await new HiLoService(store).ActAsync("alice", start.Round.RoundId,
            new("hilo_cashout_0001", won.Round!.Revision, "cashout"));
        var (summary, payload) = await Saved(database, "hi-lo", "alice");
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));

        var wagerId = "hi-lo-" + HiLoService.Hash("alice\nhilo_start_000001");
        var wager = database.Collection("balanceTransactions").Document(wagerId);
        await wager.UpdateAsync("amountCents", -99L);
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-ledger-mismatch");
        await wager.UpdateAsync("amountCents", -100L);

        var payoutId = "hi-lo-" + HiLoService.Hash("alice\nhilo_cashout_0001");
        var payout = database.Collection("balanceTransactions").Document(payoutId);
        var payoutFields = (await payout.GetSnapshotAsync()).ToDictionary();
        await payout.DeleteAsync();
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-ledger-missing");
        await payout.CreateAsync(payoutFields);

        var extra = database.Collection("balanceTransactions").Document("hi-lo-extra-" + Guid.NewGuid().ToString("N"));
        await extra.CreateAsync(new { roundId = summary.RoundId });
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-ledger-extra");
        await extra.DeleteAsync();

        var result = database.Collection("cardGameResults").Document("hi-lo-" + summary.RoundId);
        await result.UpdateAsync("payoutCents", 1L);
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-source-result-mismatch");
    }

    [Fact]
    public async Task Real_HouseHoldem_round_reconciles_action_keys_result_and_all_wallet_entries()
    {
        var database = Database();
        await SeedBalance(database);
        var cards = new[] { "A|spades", "K|clubs", "A|hearts", "Q|diamonds", "2|clubs",
            "5|diamonds", "8|hearts", "J|spades", "9|clubs" };
        var deck = cards.Concat(StandardDeck.Create().Select(CardCode.Format)
            .Where(card => !cards.Contains(card, StringComparer.Ordinal))).ToArray();
        var store = new HouseHoldemFirestoreStore(database);
        var start = await store.StartAsync("alice", "house_start_00001", 100, 0, deck,
            new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero), default);
        await store.TransitionAsync("alice", start.RoundId, "house_play_000001", HouseHoldemActions.Play4X,
            new DateTimeOffset(2026, 10, 26, 12, 0, 1, TimeSpan.Zero), default);
        await store.TransitionAsync("alice", start.RoundId, "house_advance_01", "advance",
            new DateTimeOffset(2026, 10, 26, 12, 0, 2, TimeSpan.Zero), default);
        var completed = await store.TransitionAsync("alice", start.RoundId, "house_advance_02", "advance",
            new DateTimeOffset(2026, 10, 26, 12, 0, 3, TimeSpan.Zero), default);
        Assert.True(completed.Round.PayoutCents > 0);
        var (summary, payload) = await Saved(database, "house-holdem", "alice");
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));

        var playId = $"house-holdem-{summary.RoundId}-{HouseHoldemFirestoreStore.LookupKey("house_play_000001")}-wager";
        var play = database.Collection("balanceTransactions").Document(playId);
        await play.UpdateAsync("amount", -3d);
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-ledger-mismatch");
        await play.UpdateAsync("amount", -4d);

        var payoutId = $"house-holdem-{summary.RoundId}-payout";
        var payout = database.Collection("balanceTransactions").Document(payoutId);
        var payoutFields = (await payout.GetSnapshotAsync()).ToDictionary();
        await payout.DeleteAsync();
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-ledger-missing");
        await payout.CreateAsync(payoutFields);

        var extra = database.Collection("balanceTransactions").Document($"house-holdem-{summary.RoundId}-extra");
        await extra.CreateAsync(new { userId = "alice", amount = 1d });
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-ledger-extra");
        await extra.DeleteAsync();

        var actionEvent = database.Collection("houseHoldemRoundEvents")
            .Document($"{summary.RoundId}-{HouseHoldemFirestoreStore.LookupKey("house_play_000001")}");
        await actionEvent.UpdateAsync("command", "check");
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-event-mismatch");
    }

    [Theory]
    [InlineData("casino-war")]
    [InlineData("video-poker")]
    [InlineData("baccarat")]
    [InlineData("dragon-tiger")]
    [InlineData("andar-bahar")]
    public async Task Real_paid_card_source_round_matches_private_evidence_and_tampering_is_flagged(string game)
    {
        var database = Database();
        await SeedBalance(database);
        var now = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var standard = StandardDeck.Create();
        switch (game)
        {
            case "casino-war":
                var player = new PlayingCard(CardRank.Two, CardSuit.Clubs);
                var dealer = new PlayingCard(CardRank.Ace, CardSuit.Diamonds);
                var cards = new[] { player, dealer }.Concat(standard.Where(card => card != player && card != dealer))
                    .Take(10).ToArray();
                await new CasinoWarFirestoreStore(database).StartAsync("alice", "casino_source_0001",
                    100, 0, cards, now, default);
                break;
            case "video-poker":
                var video = new VideoPokerFirestoreStore(database);
                var dealt = await video.StartAsync("alice", "video_source_00001", 1, 1,
                    [standard], now, default);
                await video.DrawAsync("alice", dealt.RoundId, "video_draw_000001",
                    new VideoPokerHeldCardPositions([]), now.AddSeconds(1), default);
                break;
            case "baccarat":
                await new BaccaratFirestoreStore(database).StartAsync("alice", "baccarat_source_01",
                    FortuneForge.Games.Baccarat.BaccaratBetSide.Player, 100, 0, 0,
                    Enumerable.Range(0, 8).SelectMany(_ => standard).ToArray(), now, default);
                break;
            case "dragon-tiger":
                await new DragonTigerFirestoreStore(database).StartAsync("alice", "dragon_source_0001",
                    new DragonTigerWagers(100, 0, 0),
                    Enumerable.Range(0, 8).SelectMany(_ => standard).ToArray(), now, default);
                break;
            case "andar-bahar":
                await new AndarBaharFirestoreStore(database).StartAsync("alice", "andar_source_00001",
                    AndarBaharSide.Andar, 100, standard, now, default);
                break;
        }
        var (summary, payload) = await Saved(database, game, "alice");
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));
        using var document = JsonDocument.Parse(payload);
        var source = database.Collection(document.RootElement.GetProperty("sourceCollection").GetString()!)
            .Document(summary.RoundId);
        if (game == "andar-bahar")
        {
            var snapshot = await source.GetSnapshotAsync();
            var json = JsonNode.Parse(snapshot.GetValue<string>("recordJson"))!;
            json["TotalReturnCents"] = 999L;
            await source.UpdateAsync("recordJson", json.ToJsonString());
        }
        else
        {
            var field = game switch
            {
                "casino-war" => "primaryStakeCents", "video-poker" => "payoutCents",
                "baccarat" => "combinedTotalReturnCents", _ => "totalReturnCents"
            };
            await source.UpdateAsync(field, 999L);
        }
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default),
            "paid-card-source-round-mismatch");
    }

    private static void AssertCode(IReadOnlyList<RoundAuditFinding> findings, string code) =>
        Assert.Contains(findings, finding => finding.Code == code && finding.Severity == "error");

    private static async Task SeedBalance(FirestoreDb database) =>
        await database.Collection("userBalances").Document("alice_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["available"] = 100L, ["availableFractionalCents"] = 0L, ["version"] = 1L
        });

    private static async Task<(RoundAuditSummary Summary, string Payload)> Saved(
        FirestoreDb database, string game, string user)
    {
        var audit = Assert.Single((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        var evidence = await database.Collection("gameRoundEvidence").Document(audit.Id).GetSnapshotAsync();
        Assert.Equal(game, audit.GetValue<string>("gameId"));
        Assert.True(evidence.Exists);
        var payload = evidence.GetValue<string>("payloadJson");
        using var document = JsonDocument.Parse(payload);
        var wager = document.RootElement.GetProperty("wagerCents").GetInt64();
        var payout = document.RootElement.GetProperty("payoutCents").GetInt64();
        var summary = new RoundAuditSummary(audit.Id, game, audit.GetValue<string>("roundId"),
            audit.GetValue<string>("tableId"), 1,
            audit.GetValue<Timestamp>("startedAtUtc").ToDateTime(),
            audit.GetValue<Timestamp>("completedAtUtc").ToDateTime(),
            audit.GetValue<string>("auditStatus"), [],
            [new(user, user, true, wager, payout, payout - wager)], true, null,
            audit.GetValue<string>("auditorVersion"), null,
            audit.GetValue<string>("evidenceSha256"), audit.GetValue<string>("evidenceKind"));
        Assert.True(PaidCardEvidenceReplayAudit.Supports(summary));
        return (summary, payload);
    }

    private static RoundAuditCapture Capture(string game, string round, long wager, long payout, object data,
        string evidenceKind = "test-evidence-v1") =>
        SinglePlayerRoundRecord.Create(game, round, "alice", wager, payout,
            new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc),
            "source", round, data, evidenceKind).Audit;

    private static async Task<DocumentReference> Ledger(FirestoreDb database, string id, string type, long cents)
    {
        var reference = database.Collection("balanceTransactions").Document(id);
        await reference.CreateAsync(new Dictionary<string, object>
        {
            ["transactionId"] = id, ["userId"] = "alice", ["currencyId"] = "slotsCredits",
            ["type"] = type, ["amount"] = cents / 100d
        });
        return reference;
    }
}
