using System.Text.Json;
using System.Text.Json.Nodes;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
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
using FortuneForge.Server.RoundAudit;
using FortuneForge.Server.SecurityAudit;
using FortuneForge.Server.Tests.Accounts;
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
        var wallet = new GameWalletFirestoreTestClient(database, "hi-lo");
        var store = new HiLoFirestoreStore(database, wallet, wallet);
        var start = await new HiLoService(store, draw: () => new(CardRank.Five, CardSuit.Clubs))
            .StartAsync("alice", new("hilo_start_000001", 100));
        var won = await new HiLoService(store, draw: () => new(CardRank.King, CardSuit.Hearts))
            .ActAsync("alice", start.Round!.RoundId, new("hilo_guess_000001", 0, "higher"));
        await new HiLoService(store).ActAsync("alice", start.Round.RoundId,
            new("hilo_cashout_0001", won.Round!.Revision, "cashout"));
        var (summary, payload) = await Saved(database, "hi-lo", "alice");
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));

        var reservationId = GameWalletSettlementIdentity.CreateId("hi-lo", "alice", "round-" + summary.RoundId);
        var wagerId = reservationId + "-wager";
        var wager = database.Collection("balanceTransactions").Document(wagerId);
        await wager.UpdateAsync("amountCents", -99L);
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-financial-evidence-invalid");
        await wager.UpdateAsync("amountCents", -100L);

        var payoutId = reservationId + "-payout";
        var payout = database.Collection("balanceTransactions").Document(payoutId);
        var payoutFields = (await payout.GetSnapshotAsync()).ToDictionary();
        await payout.DeleteAsync();
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-financial-evidence-invalid");
        await payout.CreateAsync(payoutFields);

        var guessHash = HiLoService.Hash("alice\nhilo_guess_000001");
        var guess = database.Collection("hiLoEvents").Document(guessHash);
        var guessSettlementId = (await guess.GetSnapshotAsync()).GetValue<string>("walletSettlementId");
        await guess.UpdateAsync("walletSettlementId", reservationId);
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-operation-mismatch");
        await guess.UpdateAsync("walletSettlementId", guessSettlementId);
        var guessSettlement = database.Collection("walletRoundSettlements").Document(guessSettlementId);
        var guessReceipt = (await guessSettlement.GetSnapshotAsync()).ToDictionary();
        await guessSettlement.DeleteAsync();
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-financial-evidence-invalid");
        await guessSettlement.CreateAsync(guessReceipt);

        var extra = database.Collection("balanceTransactions").Document("hi-lo-extra-" + Guid.NewGuid().ToString("N"));
        await extra.CreateAsync(new { roundId = summary.RoundId });
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-ledger-extra");
        await extra.DeleteAsync();

        var result = database.Collection("cardGameResults").Document("hi-lo-" + summary.RoundId);
        await result.UpdateAsync("payoutCents", 1L);
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-source-result-mismatch");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HiLo_reconciles_the_legacy_start_ledger_with_either_generation_of_terminal_action(bool nativeTerminal)
    {
        var database = Database();
        await SeedBalance(database);
        var wallet = new GameWalletFirestoreTestClient(database, "hi-lo");
        var store = new HiLoFirestoreStore(database, wallet, wallet);
        const string startId = "legacy_start_0001";
        const string guessId = "legacy_guess_0001";
        const string cashoutId = "legacy_cashout_01";
        var start = await new HiLoService(store, draw: () => new(CardRank.Five, CardSuit.Clubs))
            .StartAsync("alice", new(startId, 100));
        var roundId = start.Round!.RoundId;
        var head = database.Collection("hiLoPlayers").Document(HiLoService.Hash("alice"));
        await head.UpdateAsync(new Dictionary<string, object>
        {
            ["walletReservationOperationId"] = FieldValue.Delete, ["walletReservationSha256"] = FieldValue.Delete,
        });
        await RestoreLegacyHiLoAction(database, roundId, startId);

        var won = await new HiLoService(store, draw: () => new(CardRank.King, CardSuit.Hearts))
            .ActAsync("alice", roundId, new(guessId, 0, "higher"));
        await new HiLoService(store).ActAsync("alice", roundId, new(cashoutId, won.Round!.Revision, "cashout"));
        if (!nativeTerminal)
        {
            await RestoreLegacyHiLoAction(database, roundId, guessId);
            await RestoreLegacyHiLoAction(database, roundId, cashoutId);
            await database.Collection("cardGameResults").Document("hi-lo-" + roundId).UpdateAsync("schemaVersion", 1L);
        }

        var (summary, payload) = await Saved(database, "hi-lo", "alice");
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));
        var oldWager = database.Collection("balanceTransactions").Document("hi-lo-" + HiLoService.Hash("alice\n" + startId));
        await oldWager.DeleteAsync();
        AssertCode(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default), "paid-card-ledger-missing");
    }

    private static async Task RestoreLegacyHiLoAction(FirestoreDb database, string roundId, string operationId)
    {
        var hash = HiLoService.Hash("alice\n" + operationId);
        var saved = await database.Collection("hiLoOperations").Document(hash).GetSnapshotAsync();
        var receipt = JsonSerializer.Deserialize<GameWalletSettlementReceipt>(saved.GetValue<string>("walletReceiptJson"))!;
        var delta = saved.GetValue<long>("walletDeltaCents");
        if (delta != 0)
        {
            var id = "hi-lo-" + hash;
            var ledgers = database.Collection("balanceTransactions");
            await ledgers.Document(id).CreateAsync(new Dictionary<string, object>
            {
                ["transactionId"] = id, ["roundId"] = roundId, ["userId"] = "alice", ["currencyId"] = "slotsCredits",
                ["type"] = delta < 0 ? "hi-lo-wager" : "hi-lo-payout", ["idempotencyKey"] = operationId,
                ["amount"] = delta / 100d, ["amountCents"] = delta, ["balanceAfter"] = receipt.BalanceAfterCents / 100d,
                ["balanceAfterCents"] = receipt.BalanceAfterCents, ["createdAt"] = saved.GetValue<Timestamp>("createdAt"),
            });
            await ledgers.Document(receipt.WagerTransactionId ?? receipt.PayoutTransactionId!).DeleteAsync();
        }
        await database.Collection("walletRoundSettlements").Document(receipt.SettlementId).DeleteAsync();
        await database.Collection("hiLoEvents").Document(hash).UpdateAsync(new Dictionary<string, object>
        {
            ["schemaVersion"] = 1L, ["walletSettlementId"] = FieldValue.Delete,
        });
        await saved.Reference.UpdateAsync(new Dictionary<string, object>
        {
            ["schemaVersion"] = 1L, ["intentJson"] = FieldValue.Delete, ["walletReceiptJson"] = FieldValue.Delete,
            ["status"] = FieldValue.Delete,
        });
    }

    [Fact]
    public async Task Historical_HouseHoldem_round_reconciles_action_keys_result_and_all_wallet_entries()
    {
        var database = Database();
        var cards = new[] { "A|spades", "K|clubs", "A|hearts", "Q|diamonds", "2|clubs",
            "5|diamonds", "8|hearts", "J|spades", "9|clubs" };
        var deck = cards.Concat(StandardDeck.Create().Select(CardCode.Format)
            .Where(card => !cards.Contains(card, StringComparer.Ordinal))).ToArray();
        const string startKey = "house_start_00001";
        const string playKey = "house_play_000001";
        var (summary, payload) = await SeedHistoricalHouseHoldemAsync(database, deck, startKey, playKey);
        Assert.Empty(await PaidCardFinancialAudit.InspectAsync(database, summary, payload, default));

        var playId = $"house-holdem-{summary.RoundId}-{HouseHoldemRoundCodec.Hash(playKey)}-wager";
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
            .Document($"{summary.RoundId}-{HouseHoldemRoundCodec.Hash(playKey)}");
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
                await SeedHistoricalCasinoWarAsync(database, cards, now);
                break;
            case "video-poker":
                await SeedHistoricalVideoPokerAsync(database, standard, now);
                break;
            case "baccarat":
                var wallet = new GameWalletFirestoreTestClient(database, "baccarat");
                await new BaccaratFirestoreStore(database, wallet, wallet).StartAsync("alice", "baccarat_source_01",
                    FortuneForge.Games.Baccarat.BaccaratBetSide.Player, 100, 0, 0,
                    Enumerable.Range(0, 8).SelectMany(_ => standard).ToArray(), now, default);
                break;
            case "dragon-tiger":
                await SeedHistoricalDragonTigerAsync(database, standard.Take(2).ToArray(), now);
                break;
            case "andar-bahar":
                await SeedHistoricalAndarBaharAsync(database, standard, now);
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

    private static async Task<(RoundAuditSummary Summary, string Payload)> SeedHistoricalHouseHoldemAsync(
        FirestoreDb database, string[] deck, string startKey, string playKey)
    {
        const string game = "house-holdem";
        const string actor = "alice";
        const string terminalKey = "house_advance_02";
        var started = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        var completed = started.AddSeconds(3);
        var id = HouseHoldemRoundCodec.Hash($"{actor}\n{startKey}");
        string[] steps = [HouseHoldemActions.Play4X, "advance", "advance"];
        var round = HouseHoldemRules.RestoreRound(deck, 100, 0);
        foreach (var step in steps) HouseHoldemRoundCodec.Apply(round, step);
        Assert.True(round.PayoutCents > 0);

        var historical = HouseHoldemRoundCodec.Write(new(id, actor, round, deck, steps, started), completed, startKey);
        historical.Remove("backendOwner");
        historical.Remove("privateRoundJson");
        await database.Collection("houseHoldemRounds").Document(id).CreateAsync(historical);
        var resultId = $"{game}-{id}";
        await database.Collection("cardGameResults").Document(resultId).CreateAsync(new Dictionary<string, object>
        {
            ["resultId"] = resultId, ["game"] = game, ["mode"] = "credit", ["userId"] = actor,
            ["matchId"] = id, ["wagerCents"] = round.TotalWagerCents, ["payoutCents"] = round.PayoutCents,
            ["netCents"] = round.PayoutCents - round.TotalWagerCents, ["outcome"] = round.Settlement!.Outcome,
            ["settlementStatus"] = "paid", ["claimStatus"] = "completed", ["schemaVersion"] = 1L,
            ["startedAt"] = Timestamp.FromDateTime(started.UtcDateTime), ["completedAt"] = Timestamp.FromDateTime(completed.UtcDateTime),
        });
        var keys = new[] { startKey, playKey, "house_advance_01", terminalKey };
        var commands = new[] { "start" }.Concat(steps).ToArray();
        for (var index = 0; index < keys.Length; index++)
        {
            await database.Collection("houseHoldemRoundEvents").Document($"{id}-{HouseHoldemRoundCodec.Hash(keys[index])}")
                .CreateAsync(new Dictionary<string, object>
                {
                    ["roundId"] = id, ["userId"] = actor, ["command"] = commands[index], ["idempotencyKey"] = keys[index],
                    ["createdAt"] = Timestamp.FromDateTime(started.AddSeconds(index).UtcDateTime), ["schemaVersion"] = 1L,
                });
        }
        var initial = checked(round.AnteCents + round.BlindCents + round.TripsCents);
        await HistoricalWalletLegAsync(database, $"{game}-{id}-initial-wager", "house-holdem-wager", startKey,
            -initial, 10_000 - initial, started);
        await HistoricalWalletLegAsync(database, $"{game}-{id}-{HouseHoldemRoundCodec.Hash(playKey)}-wager", "house-holdem-play-wager", playKey,
            -round.PlayWagerCents, 10_000 - round.TotalWagerCents, started.AddSeconds(1));
        await HistoricalWalletLegAsync(database, $"{game}-{id}-payout", "house-holdem-payout", terminalKey + "-payout",
            round.PayoutCents, 10_000 - round.TotalWagerCents + round.PayoutCents, completed);

        var audit = SinglePlayerRoundRecord.Create(game, id, actor, round.TotalWagerCents, round.PayoutCents,
            started.UtcDateTime, completed.UtcDateTime, "houseHoldemRounds", id, new { round, steps },
            "house-holdem-cards-and-actions-v1").Audit;
        Assert.True(PaidCardEvidenceReplayAudit.Supports(audit.Summary));
        return (audit.Summary, audit.PayloadJson);
    }

    private static async Task SeedHistoricalCasinoWarAsync(FirestoreDb database, PlayingCard[] cards, DateTimeOffset now)
    {
        const string actor = "alice";
        const string key = "casino_source_0001";
        var id = CasinoWarRoundCodec.Hash($"{actor}\n{key}");
        var state = new CasinoWarSavedRound(id, actor, 100, 0, cards.Select(CardCode.Format).ToArray(), null, key, null, now);
        var round = state.Restore();
        Assert.Equal(0m, round.Round.Settlement!.TotalReturn);
        var legacy = CasinoWarRoundCodec.Write(state, now);
        legacy.Remove("backendOwner");
        await database.Collection(CasinoWarRoundCodec.Rounds).Document(id).CreateAsync(legacy);
        await HistoricalWalletLegAsync(database, $"casino-war-{id}-opening-wager", "casino-war-opening-wager", key, -100, 9_900, now);
        var audit = SinglePlayerRoundRecord.Create("casino-war", id, actor, 100, 0, now.UtcDateTime, now.UtcDateTime,
            CasinoWarRoundCodec.Rounds, id, round, "casino-war-cards-and-decision-v1").Audit;
        await WriteHistoricalAuditAsync(database, audit);
    }

    private static async Task SeedHistoricalDragonTigerAsync(FirestoreDb database, PlayingCard[] cards, DateTimeOffset now)
    {
        const string actor = "alice";
        const string key = "dragon_source_0001";
        const string collection = "dragonTigerRounds";
        var id = DragonTigerService.LookupKey(actor, key);
        var wagers = new DragonTigerWagers(100, 0, 0);
        var round = DragonTigerRules.Deal(cards);
        var returned = DragonTigerRules.Settle(wagers, round).Sum(bet => bet.TotalReturnCents);
        var stored = new DragonTigerStoredRound(id, actor, wagers, round, 2, DragonTigerRules.ShoeSize - 2);
        var timestamp = Timestamp.FromDateTime(now.UtcDateTime);
        var afterWager = 10_000 - wagers.TotalCents;
        await database.Collection(collection).Document(id).CreateAsync(new Dictionary<string, object>
        {
            ["roundId"] = id, ["userId"] = actor, ["dragonStakeCents"] = wagers.DragonCents,
            ["tigerStakeCents"] = wagers.TigerCents, ["tieStakeCents"] = wagers.TieCents,
            ["totalStakeCents"] = wagers.TotalCents, ["dragonCard"] = CardCode.Format(round.DragonCard),
            ["tigerCard"] = CardCode.Format(round.TigerCard), ["outcome"] = round.Outcome.ToString().ToLowerInvariant(),
            ["totalReturnCents"] = returned, ["profitCents"] = returned - wagers.TotalCents,
            ["balanceAfterWagerCents"] = afterWager, ["balanceAfterSettlementCents"] = afterWager + returned,
            ["shoeCardsUsed"] = (long)stored.ShoeCardsUsed, ["shoeCardsRemaining"] = (long)stored.ShoeCardsRemaining,
            ["startIdempotencyKey"] = key, ["createdAt"] = timestamp, ["schemaVersion"] = 1L,
        });
        await HistoricalWalletLegAsync(database, $"dragon-tiger-{id}-wager", "dragon-tiger-wager", key, -wagers.TotalCents, afterWager, now);
        if (returned > 0)
            await HistoricalWalletLegAsync(database, $"dragon-tiger-{id}-payout", "dragon-tiger-payout", key + "-payout", returned, afterWager + returned, now);
        await WriteHistoricalCardEventAsync(database, "dragonTigerRoundEvents", id, key, timestamp);
        Assert.NotNull(await new DragonTigerFirestoreStore(database).GetAsync(actor, id, default));
        var audit = SinglePlayerRoundRecord.Create("dragon-tiger", id, actor, wagers.TotalCents, returned,
            now.UtcDateTime, now.UtcDateTime, collection, id, stored, "dragon-tiger-cards-and-wagers-v1",
            DragonTigerOutcomeAudit.Verify(stored, wagers.TotalCents, returned), DragonTigerOutcomeAudit.Version).Audit;
        await WriteHistoricalAuditAsync(database, audit);
    }

    private static async Task SeedHistoricalVideoPokerAsync(FirestoreDb database, IReadOnlyList<PlayingCard> deck, DateTimeOffset openedAt)
    {
        const string actor = "alice";
        const string openingKey = "video_source_00001";
        const string drawKey = "video_draw_000001";
        const string collection = "videoPokerRounds";
        var id = VideoPokerFirestoreStore.CreateLookupKey($"{actor}\n{openingKey}");
        var completedAt = openedAt.AddSeconds(1);
        var dealt = VideoPokerRoundEngine.Deal([deck], 1);
        var settled = VideoPokerRoundEngine.Draw(dealt, new VideoPokerHeldCardPositions([]));
        var wager = VideoPokerMoney.WagerCents(dealt.CoinsWagered, dealt.HandCount);
        var payout = checked(settled.Results.Sum(result => result.PaytableOutcome.CreditsWon) * VideoPokerMoney.CoinValueCents);
        await database.Collection(collection).Document(id).CreateAsync(new Dictionary<string, object>
        {
            ["roundId"] = id, ["userId"] = actor, ["coinsWagered"] = (long)dealt.CoinsWagered, ["handCount"] = (long)dealt.HandCount,
            ["decks"] = deck.Select(CardCode.Format).ToArray(), ["status"] = "completed", ["startIdempotencyKey"] = openingKey,
            ["heldPositions"] = Array.Empty<long>(), ["drawIdempotencyKey"] = drawKey, ["payoutCents"] = payout,
            ["handRank"] = VideoPokerService.HandRankName(settled.Result!.HandRank),
            ["handRanks"] = settled.Results.Select(result => VideoPokerService.HandRankName(result.HandRank)).ToArray(),
            ["handPayoutCents"] = settled.Results.Select(result => result.PaytableOutcome.CreditsWon * VideoPokerMoney.CoinValueCents).ToArray(),
            ["createdAt"] = Timestamp.FromDateTime(openedAt.UtcDateTime), ["updatedAt"] = Timestamp.FromDateTime(completedAt.UtcDateTime),
            ["schemaVersion"] = 2L,
        });
        await HistoricalWalletLegAsync(database, $"video-poker-{id}-wager", "video-poker-wager", openingKey,
            -wager, 10_000 - wager, openedAt);
        if (payout > 0)
            await HistoricalWalletLegAsync(database, $"video-poker-{id}-payout", "video-poker-payout", drawKey + "-payout",
                payout, 10_000 - wager + payout, completedAt);
        var audit = SinglePlayerRoundRecord.Create("video-poker", id, actor, wager, payout,
            openedAt.UtcDateTime, completedAt.UtcDateTime, collection, id, settled, "video-poker-deal-holds-and-draw-v1").Audit;
        await WriteHistoricalAuditAsync(database, audit);
    }

    private static async Task SeedHistoricalAndarBaharAsync(FirestoreDb database, IReadOnlyList<PlayingCard> deck, DateTimeOffset now)
    {
        const string actor = "alice";
        const string key = "andar_source_00001";
        const string collection = "andarBaharRounds";
        const long stake = 100;
        const long afterWager = 9_900;
        var id = AndarBaharService.LookupKey(actor, key);
        var side = AndarBaharSide.Andar;
        var round = AndarBaharRules.Deal(deck);
        var settlement = AndarBaharRules.Settle(side, stake, round);
        var returned = settlement.TotalReturnCents;
        var stored = new AndarBaharStoredRound(id, actor, key, side, stake, round, afterWager, afterWager + returned, now);
        var sideName = side.ToString().ToLowerInvariant();
        var winner = round.Winner.ToString().ToLowerInvariant();
        var timestamp = Timestamp.FromDateTime(now.UtcDateTime);
        var record = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, RoundId = id, UserId = actor, IdempotencyKey = key, Side = sideName, StakeCents = stake,
            RevealedCards = new[] { round.Joker }.Concat(round.Deals.Select(deal => deal.Card)).Select(CardCode.Format).ToArray(),
            Winner = winner, settlement.ProfitCents, settlement.TotalReturnCents,
            stored.BalanceAfterWagerCents, stored.BalanceAfterSettlementCents, stored.CreatedAtUtc,
        });
        await database.Collection(collection).Document(id).CreateAsync(new Dictionary<string, object>
        {
            ["roundId"] = id, ["userId"] = actor, ["schemaVersion"] = 2L, ["recordJson"] = record, ["createdAt"] = timestamp,
        });
        var resultId = $"andar-bahar-{id}";
        await database.Collection("cardGameResults").Document(resultId).CreateAsync(new Dictionary<string, object>
        {
            ["resultId"] = resultId, ["game"] = "andar-bahar", ["mode"] = "credit", ["userId"] = actor, ["matchId"] = id,
            ["wagerCents"] = stake, ["payoutCents"] = returned, ["netCents"] = settlement.ProfitCents,
            ["outcome"] = settlement.Disposition.ToString().ToLowerInvariant(), ["side"] = sideName, ["winner"] = winner,
            ["settlementStatus"] = "paid", ["claimStatus"] = "completed", ["schemaVersion"] = 1L,
            ["startedAt"] = timestamp, ["completedAt"] = timestamp,
        });
        await HistoricalWalletLegAsync(database, $"{resultId}-wager", "andar-bahar-wager", key, -stake, afterWager, now, id);
        if (returned > 0)
            await HistoricalWalletLegAsync(database, $"{resultId}-payout", "andar-bahar-payout", key + "-payout", returned, afterWager + returned, now, id);
        await WriteHistoricalCardEventAsync(database, "andarBaharRoundEvents", id, key, timestamp);
        Assert.NotNull(await new AndarBaharFirestoreStore(database).GetAsync(actor, id, default));
        var audit = SinglePlayerRoundRecord.Create("andar-bahar", id, actor, stake, returned,
            now.UtcDateTime, now.UtcDateTime, collection, id, stored, "andar-bahar-deal-sequence-v1",
            AndarBaharOutcomeAudit.Verify(stored, returned), AndarBaharOutcomeAudit.Version).Audit;
        await WriteHistoricalAuditAsync(database, audit);
    }

    private static Task WriteHistoricalCardEventAsync(FirestoreDb database, string collection, string id, string key, Timestamp timestamp) =>
        database.Collection(collection).Document(id + "-settled").CreateAsync(new Dictionary<string, object>
        {
            ["roundId"] = id, ["userId"] = "alice", ["event"] = "settled", ["idempotencyKey"] = key,
            ["createdAt"] = timestamp, ["schemaVersion"] = 1L,
        });

    private static Task WriteHistoricalAuditAsync(FirestoreDb database, RoundAuditCapture audit) =>
        database.RunTransactionAsync(transaction =>
        {
            RoundAuditFirestore.Write(transaction, database, audit);
            return Task.FromResult(true);
        });

    private static Task HistoricalWalletLegAsync(FirestoreDb database, string id, string type, string key,
        long cents, long balance, DateTimeOffset when, string? andarBaharRound = null)
    {
        var fields = new Dictionary<string, object>
        {
            ["transactionId"] = id, ["userId"] = "alice", ["currencyId"] = "slotsCredits", ["type"] = type,
            ["idempotencyKey"] = key, ["amount"] = cents / 100d, ["balanceAfter"] = balance / 100d,
            ["createdAt"] = Timestamp.FromDateTime(when.UtcDateTime),
        };
        if (andarBaharRound is not null)
        {
            fields.Add("roundId", andarBaharRound);
            fields.Add("gameId", "andar-bahar");
            fields.Add("amountCents", cents);
            fields.Add("balanceAfterCents", balance);
        }
        return database.Collection("balanceTransactions").Document(id).CreateAsync(fields);
    }

    private static void AssertCode(IReadOnlyList<RoundAuditFinding> findings, string code) =>
        Assert.Contains(findings, finding => finding.Code == code && finding.Severity == "error");

    private static async Task SeedBalance(FirestoreDb database) =>
        await database.Collection("userBalances").Document("alice_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["userId"] = "alice", ["currencyId"] = "slotsCredits", ["available"] = 100L,
            ["availableFractionalCents"] = 0L, ["reserved"] = 0L, ["version"] = 1L
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
