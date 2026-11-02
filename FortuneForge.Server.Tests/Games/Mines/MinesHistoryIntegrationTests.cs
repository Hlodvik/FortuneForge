using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Mines;
using FortuneForge.Games.Mines.Server;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Cards.History;
using FortuneForge.Server.Configuration;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Mines;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class MinesHistoryIntegrationTests
{
    [Fact]
    public async Task Actual_terminal_settlement_is_readable_in_shared_history_with_exact_cents_and_no_private_tiles()
    {
        var database = Database();
        await database.Collection("userBalances").Document("player_slotsCredits").SetAsync(new Dictionary<string, object>
            { ["available"] = 100L, ["availableFractionalCents"] = 0L });
        var store = new FirestoreMinesStore(database);
        var service = new MinesService(store, new MinesEngine(new FixedBoard()));
        var start = await service.StartAsync("player", new(Guid.NewGuid().ToString("N"), 123, 3));
        await service.RevealAsync("player", start.Round!.RoundId, new(Guid.NewGuid().ToString("N"), 24));
        var request = new CashOutMinesRequest(Guid.NewGuid().ToString("N"));
        await service.CashOutAsync("player", start.Round.RoundId, request);
        var result = Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        var project = typeof(CardRoomHistoryController).GetMethod("ToPublicItem", BindingFlags.NonPublic | BindingFlags.Static)!;
        var item = Assert.IsType<CardRoomHistoryItemResponse>(project.Invoke(null, [result]));
        Assert.Equal("mines", item.Game);
        Assert.Equal("credit", item.Mode);
        Assert.Equal(1.23m, item.WagerCredits);
        Assert.Equal(1.25m, item.WinningsCredits);
        Assert.Equal(.02m, item.NetCredits);
        Assert.False(item.RequiresClaim);
        Assert.True(item.Unseen);
        foreach (var secret in new[] { "minePositions", "privateRoundJson", "revealedTiles", "responseJson", "userId" })
            Assert.DoesNotContain(secret, JsonSerializer.Serialize(item, JsonSerializerOptions.Web), StringComparison.OrdinalIgnoreCase);
        var seen = Timestamp.FromDateTime(new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc));
        await result.Reference.UpdateAsync("seenAt", seen);
        await service.CashOutAsync("player", start.Round.RoundId, request);
        Assert.Equal(seen, (await result.Reference.GetSnapshotAsync()).GetValue<Timestamp>("seenAt"));
        Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Consolation_settlement_is_visible_in_normal_and_admin_history_as_real_exact_payment()
    {
        var database = Database();
        await database.Collection("userBalances").Document("player_slotsCredits").SetAsync(new Dictionary<string, object>
            { ["available"] = 1_000L, ["availableFractionalCents"] = 0L });
        var service = new MinesService(new FirestoreMinesStore(database), new MinesEngine(new TwelveMineBoard()));
        var id = (await service.StartAsync("player", new(Guid.NewGuid().ToString("N"), 500, 12))).Round!.RoundId;
        for (var tile = 12; tile <= 16; tile++)
            await service.RevealAsync("player", id, new(Guid.NewGuid().ToString("N"), tile));
        var final = await service.RevealAsync("player", id, new(Guid.NewGuid().ToString("N"), 0));
        Assert.Equal("consolation", final.Round!.Status);
        var result = Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        var project = typeof(CardRoomHistoryController).GetMethod("ToPublicItem", BindingFlags.NonPublic | BindingFlags.Static)!;
        var item = Assert.IsType<CardRoomHistoryItemResponse>(project.Invoke(null, [result]));
        Assert.Equal("credit", item.Mode);
        Assert.Equal(5m, item.WagerCredits);
        Assert.Equal(180.41m, item.WinningsCredits);
        Assert.Equal(175.41m, item.NetCredits);
        Assert.False(item.RequiresClaim);
        Assert.Equal("consolation", result.GetValue<string>("outcome"));
        var admin = new FortuneForge.Server.Admin.Players.FirestoreAdminPlayersStore(database,
            Microsoft.Extensions.Options.Options.Create(new FortuneForge.Server.Admin.Operations.AdminOperationsOptions
            { CursorSigningKey = "mines-consolation-admin-signing-key-long-enough", MaximumDocumentsPerCollection = 1000 }));
        var match = Assert.Single((await admin.MatchesAsync("player", 25, null, default)).Items);
        Assert.Equal("mines", match.Game);
        Assert.Equal("credit", match.Mode);
        Assert.Equal(5m, match.WageredCredits);
        Assert.Equal(180.41m, match.PaidCredits);
        var gaming = (await admin.GamingAsync("player", 25, null, default)).Items;
        Assert.Equal(2, gaming.Count);
        Assert.All(gaming, value => Assert.Equal("mines", value.Game));
    }

    [Fact]
    public async Task Actual_host_resolves_only_valid_account_cookie_ignores_client_identity_and_blocks_paid_mode()
    {
        var database = Database();
        var accounts = new FirestoreAccountStore(database);
        await accounts.CreateAsync("alice", "Alice", "ALICE", "alice@example.test", "unused", "active", default);
        await database.Collection("userBalances").Document("alice_slotsCredits").SetAsync(new Dictionary<string, object>
            { ["available"] = 123L, ["availableFractionalCents"] = 45L });
        await database.Collection("userBalances").Document("attacker_slotsCredits").SetAsync(new Dictionary<string, object>
            { ["available"] = 999L, ["availableFractionalCents"] = 0L });
        var token = new string('a', 48);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        await accounts.CreateSessionAsync(hash, "alice", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), null, default);
        var accessor = new HttpContextAccessor();
        var builder = MinesHostRegistrationTests.CreateBuilder();
        builder.AddFortuneForge();
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton(new AccountService(accounts, null!, null!, accessor));
        await using var app = builder.Build();
        app.UseFortuneForgePipeline();
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
            .Single(value => value.RoutePattern.RawText == "/api/games/mines/session");
        async Task<DefaultHttpContext> Get(string? cookie, bool practice = false)
        {
            var context = new DefaultHttpContext { RequestServices = app.Services };
            context.Response.Body = new MemoryStream();
            context.Request.Headers["X-FortuneForge-Practice"] = practice ? "true" : "false";
            context.Request.Headers["X-Player-Id"] = "attacker";
            context.Request.QueryString = new QueryString("?playerId=attacker&userId=attacker");
            if (cookie is not null) context.Request.Headers.Cookie = AccountSessionCookie.Name + "=" + cookie;
            accessor.HttpContext = context;
            await endpoint.RequestDelegate!(context);
            return context;
        }
        Assert.Equal(401, (await Get(null)).Response.StatusCode);
        Assert.Equal(401, (await Get(new string('b', 48))).Response.StatusCode);
        var closed = await Get(token);
        Assert.Equal(503, closed.Response.StatusCode);
        Assert.DoesNotContain("practice", Encoding.UTF8.GetString(((MemoryStream)closed.Response.Body).ToArray()), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(403, (await Get(token, practice: true)).Response.StatusCode);
        // Enabling account-credit protocol locally must not turn a practice request into a real wager.
        app.Configuration["Features:MinesPaidEnabled"] = "true";
        var authenticated = await Get(token);
        Assert.Equal(200, authenticated.Response.StatusCode);
        Assert.Equal(403, (await Get(token, practice: true)).Response.StatusCode);
        var session = await JsonSerializer.DeserializeAsync<MinesSessionResponse>(
            new MemoryStream(((MemoryStream)authenticated.Response.Body).ToArray()), JsonSerializerOptions.Web);
        Assert.False(session!.Practice);
        Assert.Equal(12_345, session.BalanceCents);
        Assert.Empty((await database.Collection("minesRounds").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        await accounts.RevokeSessionAsync(hash, DateTime.UtcNow, default);
        Assert.Equal(401, (await Get(token)).Response.StatusCode);
    }

    private static FirestoreDb Database() => new FirestoreDbBuilder
    { ProjectId = "demo-mines-history-" + Guid.NewGuid().ToString("N"), EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
    private sealed class FixedBoard : IMinesBoardGenerator
    {
        public System.Collections.Immutable.ImmutableArray<int> Generate(int mineCount) => [0, 1, 2];
    }
    private sealed class TwelveMineBoard : IMinesBoardGenerator
    {
        public System.Collections.Immutable.ImmutableArray<int> Generate(int mineCount) => [.. Enumerable.Range(0, 12)];
    }
}
