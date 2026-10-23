using System.Reflection;
using System.Text.Json;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Admin.Players;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Admin.Players;

public sealed class AdminPlayersContractTests
{
    [Fact]
    public void Exposes_only_five_read_routes_and_no_financial_mutation()
    {
        var routes = typeof(AdminPlayersController).GetMethods().SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>()).ToArray();
        Assert.Equal(5, routes.Length);
        Assert.All(routes, route => Assert.Equal("GET", Assert.Single(route.HttpMethods)));
    }

    [Theory]
    [InlineData(false, 0, 503)]
    [InlineData(true, 1, 401)]
    [InlineData(true, 2, 403)]
    public async Task Gate_and_server_admin_role_are_enforced_before_store_resolution(bool enabled, int access, int expected)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Features:AdminOperationsEnabled"] = enabled.ToString() }).Build());
        services.AddSingleton<IAdminOperationsAuthorizer>(new Authorizer((AdminOperationsAccessStatus)access));
        var controller = new AdminPlayersController(services.BuildServiceProvider())
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(await controller.List()).StatusCode);
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(await controller.Detail("alice", default)).StatusCode);
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(await controller.Purchases("alice")).StatusCode);
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(await controller.Gaming("alice")).StatusCode);
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(await controller.Matches("alice")).StatusCode);
    }

    private sealed class Authorizer(AdminOperationsAccessStatus status) : IAdminOperationsAuthorizer
    {
        public Task<AdminOperationsAccess> AuthorizeAsync(HttpRequest request, CancellationToken ct) =>
            Task.FromResult(new AdminOperationsAccess(status, status == AdminOperationsAccessStatus.Authorized ? "admin" : null));
    }
}

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class AdminPlayersFirestoreTests
{
    [Fact]
    public async Task Profile_and_balances_preserve_cents_missing_fields_and_exclude_auth_secrets()
    {
        var (db, store) = Create();
        await User(db, "alice", "Alice", tags: ["bot"]);
        await db.Collection("userBalances").Document("alice_slotsCredits").SetAsync(new Dictionary<string, object>
            { ["available"] = 12L, ["availableFractionalCents"] = 34L });
        var detail = Assert.IsType<AdminPlayerDetail>(await store.DetailAsync("alice", default));
        Assert.Equal("alice", detail.Player.UserId);
        Assert.True(detail.Player.IsBot);
        Assert.Equal(12.34m, detail.Balances.SlotsCredits);
        Assert.Null(detail.Balances.FreeGames);
        var json = JsonSerializer.Serialize(detail);
        Assert.DoesNotContain("secret-password", json);
        Assert.DoesNotContain("passwordHash", json);
        Assert.False(detail.Coverage.Complete);
        Assert.Null(await store.DetailAsync("missing", default));
    }

    [Fact]
    public async Task Search_and_pagination_are_bounded_case_normalized_and_query_scoped()
    {
        var (db, store) = Create();
        await User(db, "a", "Alice"); await User(db, "b", "Alison"); await User(db, "c", "Bob");
        var first = await store.ListAsync("ali", 1, null, default);
        Assert.Equal("a", Assert.Single(first.Items).UserId);
        Assert.NotNull(first.NextCursor);
        var second = await store.ListAsync("ali", 1, first.NextCursor, default);
        Assert.Equal("b", Assert.Single(second.Items).UserId);
        Assert.Null(second.NextCursor);
        await Assert.ThrowsAsync<AdminOperationsQueryException>(() => store.ListAsync("bob", 1, first.NextCursor, default));
        Assert.Equal("c", Assert.Single((await store.ListAsync("c", 25, null, default)).Items).UserId);
        Assert.Equal("a", Assert.Single((await store.ListAsync("ALICE@EXAMPLE.TEST", 25, null, default)).Items).UserId);
        await Assert.ThrowsAsync<AdminOperationsQueryException>(() => store.ListAsync(null, 101, null, default));
    }

    [Fact]
    public async Task Cashflow_is_not_funding_or_house_profit_and_mock_purchase_credits_are_separate()
    {
        var (db, store) = Create(); await User(db, "alice", "Alice"); await User(db, "bob", "Bob");
        await Ledger(db, "a", "alice", "hearts-entry", -1, -100);
        await Ledger(db, "b", "alice", "hearts-queue-refund", 1, 100);
        await Ledger(db, "c", "alice", "baccarat-wager", -.25);
        await Ledger(db, "d", "alice", "baccarat-payout", .5);
        await Ledger(db, "e", "alice", "credit-purchase", 25);
        await Ledger(db, "f", "alice", "withdrawal-reservation-refund", 5);
        await Ledger(db, "g", "alice", "slot-free-games", 10, currency: "freeGames");
        await Ledger(db, "h", "bob", "slot-payout", 999);
        await Purchase(db, "purchase", "alice", 25, false);
        await Purchase(db, "mock", "alice", 100, true);
        var detail = (await store.DetailAsync("alice", default))!;
        Assert.Equal(25m, detail.Summary.PurchaseCredits);
        Assert.Equal(100m, detail.Summary.MockPurchaseCredits);
        Assert.Equal(1.25m, detail.Summary.GamingWageredCredits);
        Assert.Equal(.5m, detail.Summary.GamingPaidCredits);
        Assert.Equal(1m, detail.Summary.GamingRefundedCredits);
        Assert.Equal(.25m, detail.Summary.GamingNetCredits);
        var history = await store.GamingAsync("alice", 25, null, default);
        Assert.Equal(4, history.Items.Count);
        Assert.All(history.Items, item => Assert.Equal("alice", item.UserId));
        Assert.Contains(history.Limitations, value => value.Contains("open stakes"));
        Assert.Equal(8, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task Purchase_and_wallet_cursors_cannot_cross_player_or_history_type_and_sensitive_fields_never_escape()
    {
        var (db, store) = Create();
        await Purchase(db, "a", "alice", 1, false); await Purchase(db, "b", "alice", 2, false);
        var page = await store.PurchasesAsync("alice", 1, null, default);
        Assert.NotNull(page.NextCursor);
        Assert.Equal("a", Assert.Single(page.Items).CheckoutId);
        Assert.Equal("b", Assert.Single((await store.PurchasesAsync("alice", 1, page.NextCursor, default)).Items).CheckoutId);
        await Assert.ThrowsAsync<AdminOperationsQueryException>(() => store.PurchasesAsync("bob", 1, page.NextCursor, default));
        await Assert.ThrowsAsync<AdminOperationsQueryException>(() => store.GamingAsync("alice", 1, page.NextCursor, default));
        await Assert.ThrowsAsync<AdminOperationsQueryException>(() => store.PurchasesAsync("alice", 1, page.NextCursor + "broken", default));
        var json = JsonSerializer.Serialize(page);
        Assert.DoesNotContain("private-bank-number", json); Assert.DoesNotContain("private-provider-key", json);
    }

    [Fact]
    public async Task Match_pages_cross_sources_without_exposing_decks_or_skipping_boundary_records()
    {
        var (db, store) = Create();
        await db.Collection("cardGameResults").Document("a").SetAsync(new Dictionary<string, object>
        {
            ["userId"] = "alice", ["matchId"] = "table-one", ["game"] = "blackjack", ["wagerCents"] = 100L,
            ["payoutCents"] = 250L, ["netCents"] = 150L, ["settlementStatus"] = "paid", ["deck"] = "secret-deck"
        });
        await db.Collection("heartsPaidHistory").Document("b").SetAsync(new Dictionary<string, object>
        {
            ["userId"] = "alice", ["status"] = "result", ["matchJson"] = JsonSerializer.Serialize(new
            {
                id = "hearts-two", entryCents = 100, createdAtUtc = DateTime.UtcNow, completedAtUtc = DateTime.UtcNow,
                players = new[] { new { userId = "alice", displayName = "Alice", isAccountBacked = true, payoutCents = 400 },
                    new { userId = "bot", displayName = "Clover", isAccountBacked = false, payoutCents = 0 } },
                state = "secret-hands", decisionSeed = "secret-bot-seed"
            })
        });
        var first = await store.MatchesAsync("alice", 1, null, default);
        Assert.Equal("table-one", Assert.Single(first.Items).MatchId); Assert.NotNull(first.NextCursor);
        var second = await store.MatchesAsync("alice", 1, first.NextCursor, default);
        var hearts = Assert.Single(second.Items);
        Assert.Equal("hearts-two", hearts.MatchId); Assert.Equal(3m, hearts.NetCredits);
        Assert.True(hearts.Players![1].IsBot); Assert.Null(second.NextCursor);
        var json = JsonSerializer.Serialize(first) + JsonSerializer.Serialize(second);
        Assert.DoesNotContain("secret-", json);
        Assert.False(first.Complete);
        await Assert.ThrowsAsync<AdminOperationsQueryException>(() => store.MatchesAsync("bob", 1, first.NextCursor, default));
    }

    [Fact]
    public async Task Truncated_totals_and_unclassified_or_subcent_events_never_claim_complete_lifetime_losses()
    {
        var (db, store) = Create(100); await User(db, "alice", "Alice");
        await Task.WhenAll(Enumerable.Range(0, 102).Select(index => Ledger(db, $"event-{index:000}", "alice", "slot-wager", -1)));
        var detail = (await store.DetailAsync("alice", default))!;
        Assert.False(detail.Summary.Complete);
        Assert.Equal(100, detail.Summary.RecordsRead);
        Assert.Contains(detail.Summary.Limitations, value => value.Contains("not a lifetime total"));
        await Ledger(db, "bad-subcent", "bob", "slot-payout", .001);
        await Ledger(db, "unknown", "bob", "something-new", 5);
        var rows = await store.GamingAsync("bob", 25, null, default);
        Assert.False(rows.Complete); Assert.All(rows.Items, row => Assert.Null(row.NetCredits));
    }

    [Fact]
    public async Task New_or_wrong_sign_game_types_remain_unclassified_and_released_stakes_are_refunds_not_negative_wagers()
    {
        var (db, store) = Create(); await User(db, "alice", "Alice");
        await Ledger(db, "a", "alice", "hearts-something-new", 4);
        await Ledger(db, "b", "alice", "slot-wager", 5);
        await Ledger(db, "c", "alice", "slot-payout", -6);
        await Ledger(db, "d", "alice", "blackjack-table-wager-release", 2);
        await Ledger(db, "e", "alice", "blackjack-table-wager-adjustment", .25);
        var rows = (await store.GamingAsync("alice", 25, null, default)).Items;
        Assert.All(rows.Take(3), item => Assert.Null(item.NetCredits));
        Assert.All(rows.Skip(3), item => { Assert.Equal(0m, item.WageredCredits); Assert.Equal(0m, item.PaidCredits); });
        var detail = (await store.DetailAsync("alice", default))!;
        Assert.Equal(0m, detail.Summary.GamingWageredCredits);
        Assert.Equal(0m, detail.Summary.GamingPaidCredits);
        Assert.Equal(2.25m, detail.Summary.GamingRefundedCredits);
        Assert.Equal(2.25m, detail.Summary.GamingNetCredits);
        Assert.False(detail.Summary.Complete);
    }

    private static (FirestoreDb, FirestoreAdminPlayersStore) Create(int cap = 1000)
    {
        var db = new FirestoreDbBuilder { ProjectId = "demo-admin-players-" + Guid.NewGuid().ToString("N"), EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        return (db, new FirestoreAdminPlayersStore(db, Options.Create(new AdminOperationsOptions
            { CursorSigningKey = "admin-players-test-signing-key-long-enough", MaximumDocumentsPerCollection = cap })));
    }
    private static Task User(FirestoreDb db, string id, string name, string[]? tags = null) => db.Collection("users").Document(id).SetAsync(
        new Dictionary<string, object> { ["userId"] = id, ["playerName"] = name, ["normalizedPlayerName"] = name.ToUpperInvariant(),
            ["email"] = name.ToLowerInvariant() + "@example.test", ["status"] = "active", ["role"] = "player",
            ["passwordHash"] = "secret-password", ["profileTags"] = tags ?? [] });
    private static Task Ledger(FirestoreDb db, string id, string user, string type, double amount, long? cents = null, string currency = "slotsCredits")
    {
        var fields = new Dictionary<string, object> { ["userId"] = user, ["currencyId"] = currency, ["type"] = type,
            ["amount"] = amount, ["createdAt"] = Timestamp.GetCurrentTimestamp() };
        if (cents is not null) fields["amountCents"] = cents.Value;
        return db.Collection("balanceTransactions").Document(id).SetAsync(fields);
    }
    private static Task Purchase(FirestoreDb db, string id, string user, long credits, bool mock) => db.Collection("slotCreditPurchases").Document(id).SetAsync(
        new Dictionary<string, object> { ["userId"] = user, ["status"] = "completed", ["credits"] = credits, ["isMock"] = mock,
            ["currency"] = "ZAR", ["amountMinor"] = credits * 100, ["payerAccountNumber"] = "private-bank-number", ["providerPathwayKey"] = "private-provider-key" });
}
