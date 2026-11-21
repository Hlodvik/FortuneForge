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
using FortuneForge.Games.SicBo.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Games.SicBo.Server.Tests;

public sealed class PaidRoundTests : IClassFixture<SicBoEmulator>
{
    private readonly FirestoreDb database;
    public PaidRoundTests(SicBoEmulator emulator) => database = new FirestoreDbBuilder
    {
        ProjectId = "demo-sic-bo-" + Guid.NewGuid().ToString("N"), Endpoint = emulator.Endpoint,
        ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None,
    }.Build();

    [Fact]
    public async Task LostWalletReply_RecoversSameDiceBalanceAndHistoryAcrossConcurrentRestartsAndPause()
    {
        var wallet = new Wallet { DropNextReply = true };
        var enabled = true;
        SicBoService Service(int die = 2) => new(new SicBoWalletStore(database, wallet, wallet, () => enabled),
            TimeProvider.System, () => new(die, die, die));
        var request = new CreateSicBoRoundRequest([new("specific-double", 1, 2, null, null, null)]);
        const string key = "sic-bo-worker-request-0001";
        using (GameSecurityContext.Push(new("original-paid-request", "player", null, null, DateTime.UtcNow,
            "POST", "/api/games/sic-bo/rounds", "sic-bo", null, key, null, "{}", "captured", "test")))
            await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service().StartAsync("player", request, key, default));
        Assert.Equal(11_150, wallet.Balance);
        Assert.Empty((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        enabled = false;
        var replies = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Service(6).StartAsync("player", request, key, default)));
        Assert.All(replies, reply => Assert.Equal(JsonSerializer.Serialize(replies[0]), JsonSerializer.Serialize(reply)));
        Assert.Equal(new[] { 2, 2, 2 }, replies[0].Dice);
        Assert.Equal(12.5m, replies[0].TotalReturn);
        Assert.Equal(111.5m, replies[0].Balance);
        Assert.Equal(1, wallet.Applied);
        Assert.Single((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("sicBoRoundEvents").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("gameAnalyticsSettlementOutbox").GetSnapshotAsync()).Documents);
        var observation = Assert.Single((await database.Collection("gameSecurityEvents").GetSnapshotAsync()).Documents);
        Assert.Equal("original-paid-request", observation.GetValue<string>("requestId"));
        var history = await Service().GetAsync("player", replies[0].RoundId, default);
        Assert.Equal(JsonSerializer.Serialize(replies[0]), JsonSerializer.Serialize(history));
        await Assert.ThrowsAsync<SicBoRoundNotFoundException>(() => Service().GetAsync("attacker", history.RoundId, default));
        await Assert.ThrowsAsync<SicBoRoundConflictException>(() => Service().StartAsync("player", new([new("small", 1, null, null, null, null)]), key, default));
        await Assert.ThrowsAsync<SicBoAdmissionClosedException>(() => Service().StartAsync("player", request, key + "-next", default));
        Assert.Equal(1, wallet.Applied);
        await database.Collection("sicBoRounds").Document(replies[0].RoundId).UpdateAsync("settledBalanceCents", 999_999L);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().GetAsync("player", replies[0].RoundId, default));
        Assert.Equal(1, wallet.Applied);
    }

    [Fact]
    public async Task HistoricalLargeStake_ReplaysWithoutNewWalletSettlementOrCurrentAdmissionLimits()
    {
        const string key = "sic-bo-historical-request-0001";
        var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("player\n" + key)));
        var bets = new[] { new SicBoStoredBet("small", 5_000, null, null, null, null) };
        await database.Collection("sicBoRounds").Document(id).SetAsync(new Dictionary<string, object>
        {
            ["roundId"] = id, ["userId"] = "player", ["schemaVersion"] = 1L,
            ["betsJson"] = JsonSerializer.Serialize(bets), ["dice"] = new[] { 1, 2, 3 },
            ["totalStakeCents"] = 5_000L, ["totalReturnCents"] = 10_000L,
        });
        var wallet = new Wallet();
        var service = new SicBoService(new SicBoWalletStore(database, wallet, wallet, () => false), TimeProvider.System);
        var reply = await service.StartAsync("player", new([new("small", 50, null, null, null, null)]), key, default);
        Assert.Equal(100m, reply.TotalReturn);
        Assert.Equal(50m, reply.TotalStaked);
        await database.Collection("sicBoRounds").Document(id).UpdateAsync("dice", new[] { 0, 2, 3 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync("player", id, default));
        Assert.Equal(0, wallet.Applied);
        Assert.Empty((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task DeclinedWalletReceipt_RemainsDeclinedWithoutAnotherWalletCall()
    {
        var wallet = new Wallet { Balance = 0 };
        var service = new SicBoService(new SicBoWalletStore(database, wallet, wallet), TimeProvider.System, () => new(1, 2, 3));
        var request = new CreateSicBoRoundRequest([new("small", 1, null, null, null, null)]);
        const string key = "sic-bo-declined-request-0001";
        await Assert.ThrowsAsync<SicBoInsufficientCreditsException>(() => service.StartAsync("player", request, key, default));
        wallet.Unavailable = true;
        await Assert.ThrowsAsync<SicBoInsufficientCreditsException>(() => service.StartAsync("player", request, key, default));
        Assert.Equal(0, wallet.Applied);
        Assert.Empty((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task PublicRoutes_PreserveExactRetryAndHistoryWhileNewWagersArePaused()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Features:SicBoEnabled"] = "true";
        builder.Services.AddSingleton(database);
        builder.Services.AddSicBoWorker(builder.Configuration);
        var wallet = new Wallet();
        builder.Services.AddSingleton<IGameWalletClient>(wallet);
        builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
        await using var app = builder.Build();
        app.MapSicBoWorker(context => context.Request.Headers.ContainsKey("X-Test-Anonymous") ? null : "player");
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "sic-bo-public-route-request-0001");
        var request = new CreateSicBoRoundRequest([new("small", 1, null, null, null, null)]);
        var first = await client.PostAsJsonAsync("/api/games/sic-bo/rounds", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var body = await first.Content.ReadAsStringAsync();
        var round = (await first.Content.ReadFromJsonAsync<SicBoRoundResponse>())!;
        builder.Configuration["Features:SicBoEnabled"] = "false";
        var retry = await client.PostAsJsonAsync("/api/games/sic-bo/rounds", request);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(body, await retry.Content.ReadAsStringAsync());
        Assert.Equal(body, await client.GetStringAsync("/api/games/sic-bo/rounds/" + round.RoundId));
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "sic-bo-public-route-request-0002");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/games/sic-bo/rounds", request)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Test-Anonymous", "true");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/games/sic-bo/status")).StatusCode);
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
                var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId("sic-bo", accountId, request.OperationId),
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
