using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Keno.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Games.Keno.Server.Tests;

public sealed class PaidRoundTests : IClassFixture<KenoEmulator>
{
    private readonly FirestoreDb database;
    public PaidRoundTests(KenoEmulator emulator) => database = new FirestoreDbBuilder
    {
        ProjectId = "demo-keno-" + Guid.NewGuid().ToString("N"), Endpoint = emulator.Endpoint,
        ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None,
    }.Build();

    [Fact]
    public async Task LostWalletReply_RecoversSameDrawBalanceAndHistoryAcrossConcurrentRestartsAndPause()
    {
        var wallet = new Wallet { DropNextReply = true };
        var enabled = true;
        KenoService Service(bool nextDraw = false) => new(new KenoWalletStore(database, wallet, wallet, () => enabled),
            TimeProvider.System, () => new(Enumerable.Range(nextDraw ? 21 : 1, 20)));
        var request = new CreateKenoRoundRequest(new(new[] { 1 }), 1);
        const string key = "keno-worker-request-0001";
        using (GameSecurityContext.Push(new("original-paid-request", "player", null, null, DateTime.UtcNow,
            "POST", "/api/games/keno/rounds", "keno", null, key, null, "{}", "captured", "test")))
            await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service().StartAsync("player", request, key, default));
        Assert.Equal(10_150, wallet.Balance);
        Assert.Empty((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        enabled = false;
        var replies = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Service(true).StartAsync("player", request, key, default)));
        Assert.All(replies, reply => Assert.Equal(JsonSerializer.Serialize(replies[0]), JsonSerializer.Serialize(reply)));
        Assert.Equal(Enumerable.Range(1, 20), replies[0].Draw.Numbers);
        Assert.Equal(2.5m, replies[0].Payout);
        Assert.Equal(101.5m, replies[0].Balance);
        Assert.Equal(1, wallet.Applied);
        Assert.Single((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("kenoRoundEvents").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("gameAnalyticsSettlementOutbox").GetSnapshotAsync()).Documents);
        var observation = Assert.Single((await database.Collection("gameSecurityEvents").GetSnapshotAsync()).Documents);
        Assert.Equal("original-paid-request", observation.GetValue<string>("requestId"));
        var history = await Service().GetAsync("player", replies[0].RoundId, default);
        Assert.Equal(JsonSerializer.Serialize(replies[0]), JsonSerializer.Serialize(history));
        await Assert.ThrowsAsync<KenoRoundNotFoundException>(() => Service().GetAsync("attacker", history.RoundId, default));
        await Assert.ThrowsAsync<KenoRoundConflictException>(() => Service().StartAsync("player", request with { Wager = 2 }, key, default));
        await Assert.ThrowsAsync<KenoAdmissionClosedException>(() => Service().StartAsync("player", request, key + "-next", default));
        Assert.Equal(1, wallet.Applied);
        await database.Collection("kenoRounds").Document(replies[0].RoundId).UpdateAsync("settledBalanceCents", 999_999L);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().GetAsync("player", replies[0].RoundId, default));
        Assert.Equal(1, wallet.Applied);
    }

    [Theory]
    [InlineData(StandardKenoPaytable.LegacyId, 200L)]
    [InlineData(StandardKenoPaytable.PriorId, 400L)]
    [InlineData(StandardKenoPaytable.Id, 250L)]
    public async Task HistoricalPaytable_ReplaysItsOriginalEconomicsWithoutWalletSettlement(string paytable, long payout)
    {
        const string key = "keno-historical-request-0001";
        var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("player\n" + key)));
        await database.Collection("kenoRounds").Document(id).SetAsync(new Dictionary<string, object>
        {
            ["roundId"] = id, ["userId"] = "player", ["schemaVersion"] = 2L,
            ["ticket"] = new[] { 1 }, ["draw"] = Enumerable.Range(1, 20).ToArray(),
            ["wagerCents"] = 100L, ["payoutCents"] = payout, ["paytableId"] = paytable, ["hitCount"] = 1L,
        });
        var wallet = new Wallet();
        var service = new KenoService(new KenoWalletStore(database, wallet, wallet, () => false), TimeProvider.System);
        var reply = await service.StartAsync("player", new(new(new[] { 1 }), 1), key, default);
        Assert.Equal(payout / 100m, reply.Payout);
        await database.Collection("kenoRounds").Document(id).UpdateAsync("paytableId", "unknown-paytable");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync("player", id, default));
        Assert.Equal(0, wallet.Applied);
        Assert.Empty((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task DeclinedWalletReceipt_RemainsDeclinedWithoutAnotherWalletCall()
    {
        var wallet = new Wallet { Balance = 0 };
        var service = new KenoService(new KenoWalletStore(database, wallet, wallet), TimeProvider.System, () => new(Enumerable.Range(1, 20)));
        var request = new CreateKenoRoundRequest(new(new[] { 1 }), 1);
        const string key = "keno-declined-request-0001";
        await Assert.ThrowsAsync<KenoInsufficientCreditsException>(() => service.StartAsync("player", request, key, default));
        wallet.Unavailable = true;
        await Assert.ThrowsAsync<KenoInsufficientCreditsException>(() => service.StartAsync("player", request, key, default));
        Assert.Equal(0, wallet.Applied);
        Assert.Empty((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task PublicRoutes_PreserveExactRetryAndHistoryWhileNewWagersArePaused()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Features:KenoEnabled"] = "true";
        builder.Services.AddSingleton(database);
        builder.Services.AddKenoWorker(builder.Configuration);
        var wallet = new Wallet();
        builder.Services.AddSingleton<IGameWalletClient>(wallet);
        builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
        await using var app = builder.Build();
        app.MapKenoWorker(context => context.Request.Headers.ContainsKey("X-Test-Anonymous") ? null : "player");
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "keno-public-route-request-0001");
        var request = new CreateKenoRoundRequest(new(new[] { 1 }), 1);
        var first = await client.PostAsJsonAsync("/api/games/keno/rounds", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var body = await first.Content.ReadAsStringAsync();
        var round = (await first.Content.ReadFromJsonAsync<KenoRoundResponse>())!;
        builder.Configuration["Features:KenoEnabled"] = "false";
        var retry = await client.PostAsJsonAsync("/api/games/keno/rounds", request);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(body, await retry.Content.ReadAsStringAsync());
        Assert.Equal(body, await client.GetStringAsync("/api/games/keno/rounds/" + round.RoundId));
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "keno-public-route-request-0002");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/games/keno/rounds", request)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/games/keno/status")).StatusCode);
        Assert.Equal(1, wallet.Applied);
    }

    private sealed class Wallet : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> receipts = [];
        public long Balance { get; set; } = 10_000;
        public bool Unavailable { get; set; }
        public int Applied { get; private set; }
        public bool DropNextReply { get; set; }
        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken token) => Task.FromResult(new GameWalletAccountState(0, Balance));
        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken token)
        {
            if (Unavailable) throw new GameWalletUnavailableException();
            lock (receipts)
            {
                if (receipts.TryGetValue(request.OperationId, out var prior))
                {
                    if (prior.Request != request) throw new GameWalletConflictException();
                    return Task.FromResult(prior.Receipt with { WasAlreadySettled = true });
                }
                var before = Balance;
                var insufficient = before < request.WagerCents;
                if (!insufficient) Balance = checked(Balance - request.WagerCents + request.PayoutCents);
                var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId("keno", accountId, request.OperationId),
                    !insufficient ? "wager" : null, !insufficient && request.PayoutCents > 0 ? "payout" : null, before, insufficient ? before : before - request.WagerCents, Balance,
                    DateTimeOffset.UtcNow, false, insufficient);
                receipts.Add(request.OperationId, (request, receipt));
                if (!insufficient) Applied++;
                if (DropNextReply) { DropNextReply = false; throw new GameWalletUnavailableException(); }
                return Task.FromResult(receipt);
            }
        }
    }
}
