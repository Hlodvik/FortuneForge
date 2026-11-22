using System.Reflection;
using System.Text.Json;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.AndarBahar.Server;
using FortuneForge.Games.DragonTiger.Server;
using FortuneForge.Games.HiLo.Server;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Admin.Players;
using FortuneForge.Server.Cards.History;
using FortuneForge.Server.Tests.Accounts;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Games.HiLo;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class HiLoHistoryIntegrationTests
{
    [Fact]
    public async Task Actual_paid_settlement_is_readable_in_shared_and_admin_history_with_exact_money_but_no_private_cards()
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = "demo-hi-lo-history-" + Guid.NewGuid().ToString("N"),
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        await database.Collection("users").Document("player").SetAsync(new Dictionary<string, object>
            { ["playerName"] = "Player", ["role"] = "player" });
        await database.Collection("userBalances").Document("player_slotsCredits").SetAsync(new Dictionary<string, object>
            { ["userId"] = "player", ["currencyId"] = "slotsCredits", ["available"] = 100L,
                ["availableFractionalCents"] = 0L, ["reserved"] = 0L, ["version"] = 1L });
        var wallet = new GameWalletFirestoreTestClient(database, "hi-lo");
        var store = new HiLoFirestoreStore(database, wallet, wallet);
        var start = await new HiLoService(store, draw: () => new(CardRank.Seven, CardSuit.Clubs))
            .StartAsync("player", new(Guid.NewGuid().ToString("N"), 100));
        var win = await new HiLoService(store, draw: () => new(CardRank.King, CardSuit.Hearts))
            .ActAsync("player", start.Round!.RoundId, new(Guid.NewGuid().ToString("N"), 0, "higher"));
        var cashout = new HiLoActionRequest(Guid.NewGuid().ToString("N"), win.Round!.Revision, "cashout");
        await new HiLoService(store).ActAsync("player", start.Round.RoundId, cashout);
        var result = Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        var project = typeof(CardRoomHistoryController).GetMethod("ToPublicItem", BindingFlags.NonPublic | BindingFlags.Static)!;
        var publicResult = Assert.IsType<CardRoomHistoryItemResponse>(project.Invoke(null, [result]));
        Assert.Equal("hi-lo", publicResult.Game);
        Assert.Equal(1m, publicResult.WagerCredits);
        Assert.Equal(2.14m, publicResult.WinningsCredits);
        Assert.Equal(1.14m, publicResult.NetCredits);
        Assert.True(publicResult.Unseen);
        Assert.False(publicResult.RequiresClaim);
        var json = JsonSerializer.Serialize(publicResult, JsonSerializerOptions.Web);
        foreach (var secret in new[] { "userId", "currentCard", "history", "roundJson", "operationId", "balance" })
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);

        var admin = new FirestoreAdminPlayersStore(database, Options.Create(new AdminOperationsOptions
            { CursorSigningKey = "hi-lo-admin-test-signing-key-long-enough", MaximumDocumentsPerCollection = 1000 }));
        var match = Assert.Single((await admin.MatchesAsync("player", 25, null, default)).Items);
        Assert.Equal("hi-lo", match.Game);
        Assert.Equal(1m, match.WageredCredits);
        Assert.Equal(2.14m, match.PaidCredits);
        var walletEvents = (await admin.GamingAsync("player", 25, null, default)).Items;
        Assert.Equal(2, walletEvents.Count);
        Assert.All(walletEvents, value => Assert.Equal("hi-lo", value.Game));
        var detail = (await admin.DetailAsync("player", default))!;
        Assert.Equal(1m, detail.Summary.GamingWageredCredits);
        Assert.Equal(2.14m, detail.Summary.GamingPaidCredits);
        Assert.Equal(1.14m, detail.Summary.GamingNetCredits);

        var seen = Timestamp.FromDateTime(new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc));
        await result.Reference.UpdateAsync("seenAt", seen);
        await new HiLoService(store).ActAsync("player", start.Round.RoundId, cashout);
        Assert.Equal(seen, (await result.Reference.GetSnapshotAsync()).GetValue<Timestamp>("seenAt"));
        Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("game")]
    [InlineData("operation")]
    [InlineData("phase")]
    [InlineData("ledger-id")]
    [InlineData("amount")]
    [InlineData("money-mirror")]
    [InlineData("missing-receipt")]
    [InlineData("reservation-link")]
    [InlineData("completion-amount")]
    [InlineData("schema")]
    [InlineData("receipt-id")]
    [InlineData("unknown-scope")]
    public async Task Generic_wallet_history_keeps_unverified_or_unknown_cashflow_out_of_admin_totals(string tamper)
    {
        var game = tamper == "unknown-scope" ? "unknown-wallet-scope" : "hi-lo";
        var (database, reserved, completed) = await PostedWalletRoundAsync(game);
        var reservation = database.Collection("walletRoundSettlements").Document(reserved.SettlementId);
        var completion = database.Collection("walletRoundSettlements").Document(completed.SettlementId);
        var wager = database.Collection("balanceTransactions").Document(reserved.WagerTransactionId!);
        var payout = database.Collection("balanceTransactions").Document(completed.PayoutTransactionId!);
        switch (tamper)
        {
            case "account": await reservation.UpdateAsync("userId", "other-account"); break;
            case "game": await reservation.UpdateAsync("gameId", "mines"); break;
            case "operation": await reservation.UpdateAsync("operationId", "other-operation"); break;
            case "phase": await reservation.UpdateAsync("phase", "Direct"); break;
            case "ledger-id": await reservation.UpdateAsync("wagerTransactionId", "unrelated-ledger"); break;
            case "amount": await wager.UpdateAsync("amountCents", -99L); break;
            case "money-mirror": await wager.UpdateAsync("amount", -.99d); break;
            case "missing-receipt": await reservation.DeleteAsync(); break;
            case "reservation-link": await completion.UpdateAsync("reservationSha256", new string('c', 64)); break;
            case "completion-amount": await completion.UpdateAsync("payoutCents", 215L); break;
            case "schema": await reservation.UpdateAsync("schemaVersion", 2L); break;
            case "receipt-id": await payout.UpdateAsync("walletSettlementId", reserved.SettlementId); break;
        }
        var admin = new FirestoreAdminPlayersStore(database, Options.Create(new AdminOperationsOptions
            { CursorSigningKey = "hi-lo-admin-proof-signing-key-long-enough", MaximumDocumentsPerCollection = 1000 }));
        var history = await admin.GamingAsync("player", 25, null, default);
        Assert.Equal(2, history.Items.Count);
        Assert.False(history.Complete);
        Assert.All(history.Items, item =>
        {
            Assert.Null(item.WageredCredits);
            Assert.Null(item.PaidCredits);
            Assert.Null(item.NetCredits);
        });
        var summary = (await admin.DetailAsync("player", default))!.Summary;
        Assert.Equal(0m, summary.GamingWageredCredits);
        Assert.Equal(0m, summary.GamingPaidCredits);
        Assert.Equal(0m, summary.GamingNetCredits);
        Assert.False(summary.Complete);
    }

    [Fact]
    public async Task Cloned_wallet_ledger_documents_cannot_duplicate_verified_admin_cashflow()
    {
        var (database, reserved, completed) = await PostedWalletRoundAsync("hi-lo");
        foreach (var id in new[] { reserved.WagerTransactionId!, completed.PayoutTransactionId! })
        {
            var original = await database.Collection("balanceTransactions").Document(id).GetSnapshotAsync();
            // Every stored field, including transactionId and settlement pointers, is copied unchanged.
            await database.Collection("balanceTransactions").Document("clone-" + id).SetAsync(original.ToDictionary());
        }
        var admin = new FirestoreAdminPlayersStore(database, Options.Create(new AdminOperationsOptions
            { CursorSigningKey = "hi-lo-admin-clone-signing-key-long-enough", MaximumDocumentsPerCollection = 1000 }));
        var history = await admin.GamingAsync("player", 25, null, default);
        Assert.Equal(4, history.Items.Count);
        Assert.Equal(2, history.Items.Count(item => item.NetCredits is not null));
        Assert.All(history.Items.Where(item => item.EventId.StartsWith("clone-", StringComparison.Ordinal)), item =>
        {
            Assert.Null(item.WageredCredits);
            Assert.Null(item.PaidCredits);
            Assert.Null(item.NetCredits);
        });
        var summary = (await admin.DetailAsync("player", default))!.Summary;
        Assert.Equal(1m, summary.GamingWageredCredits);
        Assert.Equal(2.14m, summary.GamingPaidCredits);
        Assert.Equal(1.14m, summary.GamingNetCredits);
        Assert.False(summary.Complete);
    }

    [Theory]
    [InlineData("andar-bahar", false)]
    [InlineData("andar-bahar", true)]
    [InlineData("dragon-tiger", false)]
    [InlineData("dragon-tiger", true)]
    public async Task Direct_card_identity_fields_cannot_replace_a_missing_or_corrupted_wallet_receipt(string game, bool corrupt)
    {
        var (database, posted, _) = await PostedWalletRoundAsync(game, direct: true);
        var admin = new FirestoreAdminPlayersStore(database, Options.Create(new AdminOperationsOptions
            { CursorSigningKey = "direct-card-admin-proof-signing-key-long-enough", MaximumDocumentsPerCollection = 1000 }));
        var validHistory = await admin.GamingAsync("player", 25, null, default);
        Assert.Equal(2, validHistory.Items.Count);
        Assert.All(validHistory.Items, item => Assert.Matches("^[0-9a-f]{64}$", item.MatchId!));
        var validSummary = (await admin.DetailAsync("player", default))!.Summary;
        Assert.Equal(1m, validSummary.GamingWageredCredits);
        Assert.Equal(2.14m, validSummary.GamingPaidCredits);
        Assert.Equal(1.14m, validSummary.GamingNetCredits);
        var receipt = database.Collection("walletRoundSettlements").Document(posted.SettlementId);
        if (corrupt) await receipt.UpdateAsync("balanceAfterCents", posted.BalanceAfterCents + 1);
        else await receipt.DeleteAsync();
        var history = await admin.GamingAsync("player", 25, null, default);
        Assert.Equal(2, history.Items.Count);
        Assert.All(history.Items, item =>
        {
            Assert.Equal(game, item.Game);
            Assert.Null(item.WageredCredits);
            Assert.Null(item.PaidCredits);
            Assert.Null(item.NetCredits);
        });
        Assert.False(history.Complete);
        var summary = (await admin.DetailAsync("player", default))!.Summary;
        Assert.Equal(0m, summary.GamingWageredCredits);
        Assert.Equal(0m, summary.GamingPaidCredits);
        Assert.Equal(0m, summary.GamingNetCredits);
        Assert.False(summary.Complete);
    }

    private static async Task<(FirestoreDb Database, GameWalletSettlementReceipt Reserved, GameWalletSettlementReceipt Completed)>
        PostedWalletRoundAsync(string game, bool direct = false)
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = "demo-hi-lo-history-proof-" + Guid.NewGuid().ToString("N"),
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        await database.Collection("users").Document("player").SetAsync(new Dictionary<string, object>
            { ["playerName"] = "Player", ["role"] = "player" });
        await database.Collection("userBalances").Document("player_slotsCredits").SetAsync(new Dictionary<string, object>
            { ["userId"] = "player", ["currencyId"] = "slotsCredits", ["available"] = 100L,
                ["availableFractionalCents"] = 0L, ["reserved"] = 0L, ["version"] = 1L });
        var wallet = new GameWalletFirestoreTestClient(database, game);
        if (direct)
        {
            var key = Guid.NewGuid().ToString("N");
            var roundId = game switch
            {
                "andar-bahar" => AndarBaharService.LookupKey("player", key),
                "dragon-tiger" => DragonTigerService.LookupKey("player", key),
                _ => throw new ArgumentException("A direct fixture requires a real supported card-game round lookup.", nameof(game)),
            };
            var posted = await wallet.SettleAsync("player", new(roundId,
                new string('a', 64), 100, 214), default);
            return (database, posted, posted);
        }
        var reserve = new GameWalletSettlementRequest("round-" + Guid.NewGuid().ToString("N"), new string('a', 64),
            100, 0, Phase: GameWalletSettlementPhase.Reserve);
        var reserved = await wallet.SettleAsync("player", reserve, default);
        var completed = await wallet.SettleAsync("player", new(reserve.OperationId, new string('b', 64), 0, 214,
            Phase: GameWalletSettlementPhase.CompleteReservation, ReservationSha256: reserve.OutcomeSha256), default);
        return (database, reserved, completed);
    }
}
