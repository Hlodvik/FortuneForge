using System.Reflection;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Mines;
using FortuneForge.Games.Mines.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Cards.History;
using FortuneForge.Server.Configuration;
using FortuneForge.Server.Tests.Accounts;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
            { ["userId"] = "player", ["currencyId"] = "slotsCredits", ["available"] = 100L,
                ["availableFractionalCents"] = 0L, ["reserved"] = 0L, ["version"] = 1L });
        var wallet = new GameWalletFirestoreTestClient(database, "mines");
        var store = new FirestoreMinesStore(database, wallet, wallet);
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
            { ["userId"] = "player", ["currencyId"] = "slotsCredits", ["available"] = 1_000L,
                ["availableFractionalCents"] = 0L, ["reserved"] = 0L, ["version"] = 1L });
        var wallet = new GameWalletFirestoreTestClient(database, "mines");
        var service = new MinesService(new FirestoreMinesStore(database, wallet, wallet), new MinesEngine(new TwelveMineBoard()));
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
            { ["userId"] = "alice", ["currencyId"] = "slotsCredits", ["available"] = 123L,
                ["availableFractionalCents"] = 45L, ["reserved"] = 0L, ["version"] = 1L });
        await database.Collection("userBalances").Document("attacker_slotsCredits").SetAsync(new Dictionary<string, object>
            { ["available"] = 999L, ["availableFractionalCents"] = 0L });
        var token = new string('a', 48);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        await accounts.CreateSessionAsync(hash, "alice", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), null, default);
        var walletKey = RandomNumberGenerator.GetBytes(32);
        var workerKey = RandomNumberGenerator.GetBytes(32);
        var walletBuilder = LoopbackBuilder();
        walletBuilder.Configuration["GameWallet:Games:mines:SigningKey"] = Convert.ToBase64String(walletKey);
        walletBuilder.Configuration["GameWallet:Games:mines:AllowAccountBalanceRead"] = "true";
        walletBuilder.Services.AddSingleton(database);
        walletBuilder.Services.AddSingleton<IWalletRoundSettlementStore>(new FirestoreWalletRoundSettlementStore(database));
        await using var wallet = walletBuilder.Build();
        wallet.MapGameWalletSettlement();
        await wallet.StartAsync();

        var workerBuilder = LoopbackBuilder();
        workerBuilder.Configuration["GameWorker:Mode"] = "hosted";
        workerBuilder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(workerKey);
        workerBuilder.Configuration["GameWallet:Url"] = wallet.Urls.Single();
        workerBuilder.Configuration["GameWallet:SigningKey"] = Convert.ToBase64String(walletKey);
        workerBuilder.Configuration["Features:MinesEnabled"] = "true";
        workerBuilder.Configuration["Features:MinesPaidEnabled"] = "false";
        workerBuilder.Services.AddSingleton(database);
        workerBuilder.Services.AddGameWalletClient("mines");
        workerBuilder.Services.AddMinesServer(options => options.AllowedOrigins = ["https://fortuneforgegame.web.app"]);
        await using var worker = workerBuilder.Build();
        worker.UseRouting();
        worker.UseGameWorkerAuthentication("mines");
        var observedAccounts = new List<string?>();
        worker.MapMines((context, _) =>
        {
            var actor = GameWorkerAuthentication.GetAccountId(context);
            observedAccounts.Add(actor);
            return Task.FromResult<MinesAccount?>(actor is null ? null : new MinesAccount(actor));
        });
        await worker.StartAsync();

        var builder = MinesHostRegistrationTests.CreateBuilder(Environments.Development);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["LocalDevelopment:DefaultAccount:Enabled"] = "false";
        builder.Configuration["GameWorkers:mines:ForwardPublicRoutes"] = "true";
        builder.Configuration["GameWorkers:mines:Url"] = worker.Urls.Single();
        builder.Configuration["GameWorkers:mines:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.AddFortuneForge();
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton(provider => new AccountService(accounts, null!, null!, provider.GetRequiredService<IHttpContextAccessor>()));
        await using var app = builder.Build();
        app.UseFortuneForgePipeline();
        await app.StartAsync();
        using var browser = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(app.Urls.Single()) };
        async Task<HttpResponseMessage> Send(string? cookie, bool practice = false, bool start = false)
        {
            using var request = new HttpRequestMessage(start ? HttpMethod.Post : HttpMethod.Get,
                "/api/games/mines/" + (start ? "rounds" : "session") + "?playerId=attacker&userId=attacker");
            if (practice) request.Headers.Add("X-FortuneForge-Practice", "true");
            request.Headers.Add("X-Player-Id", "attacker");
            request.Headers.Add("Origin", "https://fortuneforgegame.web.app");
            if (cookie is not null) request.Headers.Add("Cookie", AccountSessionCookie.Name + "=" + cookie);
            if (start) request.Content = JsonContent.Create(new StartMinesRequest(Guid.NewGuid().ToString("N"), 100, 3));
            return await browser.SendAsync(request);
        }
        using var anonymous = await Send(null);
        using var invalid = await Send(new string('b', 48));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        using var closed = await Send(token, start: true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, closed.StatusCode);
        Assert.DoesNotContain("practice", await closed.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        using var practiceBefore = await Send(token, practice: true);
        Assert.Equal(HttpStatusCode.Forbidden, practiceBefore.StatusCode);
        // The worker owns admission; enabling the old host flag cannot admit a wager.
        app.Configuration["Features:MinesPaidEnabled"] = "true";
        using var stillClosed = await Send(token, start: true);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, stillClosed.StatusCode);
        worker.Configuration["Features:MinesPaidEnabled"] = "true";
        using var authenticated = await Send(token);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
        using var practiceAfter = await Send(token, practice: true);
        Assert.Equal(HttpStatusCode.Forbidden, practiceAfter.StatusCode);
        var session = await authenticated.Content.ReadFromJsonAsync<MinesSessionResponse>(JsonSerializerOptions.Web);
        Assert.False(session!.Practice);
        Assert.Equal(12_345, session.BalanceCents);
        Assert.Empty((await database.Collection("minesRounds").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.NotEmpty(observedAccounts);
        Assert.All(observedAccounts, actor => Assert.Equal("alice", actor));
        await accounts.RevokeSessionAsync(hash, DateTime.UtcNow, default);
        using var revoked = await Send(token);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    private static WebApplicationBuilder LoopbackBuilder()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder;
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
