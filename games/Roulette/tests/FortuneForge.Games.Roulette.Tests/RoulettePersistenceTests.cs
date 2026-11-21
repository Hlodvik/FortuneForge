using System.Net;
using System.Net.Http.Json;
using System.Text;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.PackagedGame.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using System.Text.Json;
using FortuneForge.Server.Games.Roulette;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Games.Roulette.Tests;
public sealed class RoulettePersistenceTests
{
    [Fact]
    public async Task SignedWorkerUsesOriginalMutationBodiesAndPrivateHistory()
    {
        var db = Database(); var key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(key); builder.Configuration["Features:RouletteEnabled"] = "true";
        builder.Services.AddSingleton(db); GameWorkerModule.Configure(builder); builder.Services.AddSingleton<IRoulettePopulation, NoRoulettePopulation>();
        var names = new NameReader(); builder.Services.AddSingleton<IGameWalletAccountReader>(names);
        builder.Services.AddSingleton(new FirestoreRouletteService(db, new NoRoulettePopulation(), () => builder.Configuration.GetValue("Features:RouletteEnabled", false), () => 17));
        await using var app = builder.Build(); app.UseRouting(); app.UseGameWorkerAuthentication("roulette");
        GameWorkerModule.Map(app, GameWorkerAuthentication.GetAccountId); await app.StartAsync(); var client = app.GetTestClient();
        HttpRequestMessage Signed(HttpMethod method, string path, string body = "", string user = "alice", string? operation = null)
        {
            var request = new HttpRequestMessage(method, path);
            if (body.Length > 0) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            Dictionary<string, string>? headers = operation is null ? null : new() { ["Idempotency-Key"] = operation };
            if (operation is not null) request.Headers.Add("Idempotency-Key", operation);
            request.Headers.Add(GameWorkerAuthentication.AssertionHeader, GameWorkerAuthentication.CreateAssertion("roulette", user, method.Method,
                path, Encoding.UTF8.GetBytes(body), key, DateTimeOffset.UtcNow, new FortuneForge.Games.Abstractions.Auditing.GameRequestObservation(Guid.NewGuid().ToString("N"), user, null, null, DateTime.UtcNow, method.Method, path, "roulette", null, operation, null, null, "sanitized", "direct"), headers: headers)); return request;
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/games/roulette/status")).StatusCode);
        var openedResponse = await client.SendAsync(Signed(HttpMethod.Post, "/api/games/roulette/rounds", "{}", operation: "roulette_http_open_0001"));
        Assert.Equal(HttpStatusCode.OK, openedResponse.StatusCode);
        var opened = await openedResponse.Content.ReadFromJsonAsync<RouletteRoundResponse>();
        Assert.Equal("Alice Trusted", Assert.Single(opened!.Players).DisplayName);
        var checkpoint = db.Collection("rouletteTables").Document(opened.RoundId.ToString("N"));
        var validCheckpoint = (await checkpoint.GetSnapshotAsync()).GetValue<string>("stateJson");
        await checkpoint.UpdateAsync("stateJson", "{");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(Signed(HttpMethod.Get, $"/api/games/roulette/rounds/{opened.RoundId}"))).StatusCode);
        await checkpoint.UpdateAsync("stateJson", validCheckpoint);
        var betBody = JsonSerializer.Serialize(new PlaceRouletteBetRequest("straight", 10m, 17, [], opened!.Revision), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var betResponse = await client.SendAsync(Signed(HttpMethod.Post, $"/api/games/roulette/rounds/{opened.RoundId}/bets", betBody, operation: "roulette_http_bet_0001"));
        Assert.Equal(HttpStatusCode.OK, betResponse.StatusCode); var bet = await betResponse.Content.ReadFromJsonAsync<RouletteRoundResponse>();
        var polled = await (await client.SendAsync(Signed(HttpMethod.Get, $"/api/games/roulette/rounds/{opened.RoundId}"))).Content.ReadFromJsonAsync<RouletteRoundResponse>();
        Assert.Equal(bet!.Revision, polled!.Revision);
        var spinBody = JsonSerializer.Serialize(new RouletteActionRequest(bet!.Revision)); var spinPath = $"/api/games/roulette/rounds/{opened.RoundId}/spin";
        var spun = await client.SendAsync(Signed(HttpMethod.Post, spinPath, spinBody, operation: "roulette_http_spin_0001"));
        Assert.Equal(HttpStatusCode.OK, spun.StatusCode); var original = await spun.Content.ReadAsStringAsync(); builder.Configuration["Features:RouletteEnabled"] = "false"; names.Available = false;
        var paused = await (await client.SendAsync(Signed(HttpMethod.Get, "/api/games/roulette/status"))).Content.ReadFromJsonAsync<RouletteStatusResponse>();
        Assert.False(paused!.Available);
        Assert.Equal(original, await (await client.SendAsync(Signed(HttpMethod.Post, spinPath, spinBody, operation: "roulette_http_spin_0001"))).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Signed(HttpMethod.Get, $"/api/games/roulette/rounds/{opened.RoundId}/history/1", user: "mallory"))).StatusCode);
        Assert.Equal(1, names.Calls);
        builder.Configuration["Features:RouletteEnabled"] = "true";
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(Signed(HttpMethod.Post, "/api/games/roulette/rounds", "{}", operation: "roulette_http_missing_name_0001"))).StatusCode);
        Assert.Equal(2, names.Calls);
        Assert.DoesNotContain("stateJson", original); Assert.DoesNotContain("fingerprint", original);
    }

    [Fact]
    public async Task ReplicasRecoverOneSpinAndPrivateHistoryWhileNewActionsArePaused()
    {
        var db = Database(); var accepting = true; var draws = 0;
        FirestoreRouletteService Service() => new(db, new NoRoulettePopulation(), () => accepting, () => { draws++; return 17; });
        var first = Service(); var second = Service();
        var opened = await first.StartAsync("alice", "Alice", "roulette_open_alice_0001");
        var joined = await second.StartAsync("bob", "Bob", "roulette_open_bob_0001");
        Assert.Equal(opened.RoundId, joined.RoundId); Assert.Equal(2, joined.Players.Count);
        await Assert.ThrowsAsync<RouletteConflictException>(() => first.PlaceBetAsync("alice", opened.RoundId, new("red", 1m, null, []), "roulette_stale_bet_0001", opened.Revision));
        var current = await second.GetAsync("alice", opened.RoundId);
        var request = new PlaceRouletteBetRequest("straight", 10m, 17, []);
        var bet = await first.PlaceBetAsync("alice", opened.RoundId, request, "roulette_bet_alice_0001", current.Revision);
        Assert.Equal(990m, bet.Balance);
        Assert.Equal(1000m, (await second.GetAsync("bob", opened.RoundId)).Balance);
        current = await first.GetAsync("alice", opened.RoundId);
        var settled = await second.SpinAsync("alice", opened.RoundId, "roulette_spin_alice_0001", current.Revision);
        accepting = false;
        var recovered = await first.SpinAsync("alice", opened.RoundId, "roulette_spin_alice_0001", current.Revision);
        Assert.Equal(JsonSerializer.Serialize(settled), JsonSerializer.Serialize(recovered));
        Assert.Equal(1350m, recovered.Balance); Assert.Equal(17, recovered.WinningPocket); Assert.Equal(1, draws);
        Assert.Equal(JsonSerializer.Serialize(settled), JsonSerializer.Serialize(await second.HistoryAsync("alice", opened.RoundId, 1)));
        await Assert.ThrowsAsync<RouletteAccessException>(() => second.HistoryAsync("mallory", opened.RoundId, 1));
        await Assert.ThrowsAsync<RouletteUnavailableException>(() => first.StartAsync("charlie", "Charlie", "roulette_open_charlie_0001"));
        Assert.Single((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection("gameAnalyticsSettlementOutbox").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("userBalances").GetSnapshotAsync()).Documents);
        accepting = true;
        var next = await first.StartAsync("alice", "Alice", "roulette_open_alice_0002");
        Assert.Equal(opened.RoundId, next.RoundId); Assert.Equal(2, next.RoundNumber); Assert.Equal(1350m, next.Balance); Assert.Empty(next.Bets);
        Assert.Equal(17, (await second.HistoryAsync("alice", opened.RoundId, 1)).WinningPocket);
    }

    [Fact]
    public async Task PollingAppendsEvidenceWithoutInvalidatingCommandsAndOldCheckpointsResume()
    {
        var db = Database(); var service = new FirestoreRouletteService(db, new NoRoulettePopulation(), draw: () => 17);
        var opened = await service.StartAsync("alice", "Alice", "roulette_poll_open_0001");
        var checkpoint = db.Collection("rouletteTables").Document(opened.RoundId.ToString("N"));
        var saved = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>((await checkpoint.GetSnapshotAsync()).GetValue<string>("stateJson"))!;
        saved.Remove("revision");
        await checkpoint.UpdateAsync("stateJson", JsonSerializer.Serialize(saved));
        var polled = await service.GetAsync("alice", opened.RoundId);
        Assert.Equal(opened.Revision, polled.Revision);
        var bet = await service.PlaceBetAsync("alice", opened.RoundId, new("straight", 5m, 17, []), "roulette_poll_bet_0001", opened.Revision);
        var second = new FirestoreRouletteService(db, new NoRoulettePopulation(), draw: () => 17);
        Assert.Equal(bet.Revision, (await second.GetAsync("alice", opened.RoundId)).Revision);
        Assert.Equal(bet.Revision, (await service.GetAsync("alice", opened.RoundId)).Revision);
        var settled = await second.SpinAsync("alice", opened.RoundId, "roulette_poll_spin_0001", bet.Revision);
        Assert.Equal("settled", settled.Phase); Assert.Equal(1175m, settled.Balance);
        var stored = JsonSerializer.Deserialize<RouletteGameService.RouletteTable>((await checkpoint.GetSnapshotAsync()).GetValue<string>("stateJson"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(stored.EventVersion > stored.Revision);
    }

    [Fact]
    public async Task ConcurrentBetRetriesCommitOneVirtualWagerAndKeepOriginalReceipt()
    {
        var db = Database(); var first = new FirestoreRouletteService(db, new NoRoulettePopulation());
        var opened = await first.StartAsync("alice", "Alice", "roulette_concurrent_open_0001");
        var request = new PlaceRouletteBetRequest("red", 5m, null, []);
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => new FirestoreRouletteService(db, new NoRoulettePopulation())
            .PlaceBetAsync("alice", opened.RoundId, request, "roulette_concurrent_bet_0001", opened.Revision)));
        Assert.Single(results.Select(result => JsonSerializer.Serialize(result)).Distinct()); Assert.Equal(995m, results[0].Balance);
        Assert.Single((await first.GetAsync("alice", opened.RoundId)).Bets);
        await Assert.ThrowsAsync<RouletteConflictException>(() => first.PlaceBetAsync("alice", opened.RoundId, request with { Stake = 10m }, "roulette_concurrent_bet_0001", opened.Revision));
    }

    [Fact]
    public async Task DisconnectedOpenWagersRemainRecoverableAfterMaintenanceAndRestart()
    {
        var db = Database(); var service = new FirestoreRouletteService(db, new NoRoulettePopulation(), draw: () => 0);
        var opened = await service.StartAsync("alice", "Alice", "roulette_recover_open_0001");
        await service.PlaceBetAsync("alice", opened.RoundId, new("red", 5m, null, []), "roulette_recover_bet_0001", opened.Revision);
        await service.SweepAsync(DateTime.UtcNow.AddMinutes(10));
        var replica = new FirestoreRouletteService(db, new NoRoulettePopulation(), draw: () => 0);
        var recovered = await replica.GetAsync("alice", opened.RoundId);
        Assert.Equal(995m, recovered.Balance); Assert.Single(recovered.Bets); Assert.Equal("open", recovered.Phase);
        var settled = await replica.SpinAsync("alice", opened.RoundId, "roulette_recover_spin_0001", recovered.Revision);
        Assert.Equal("settled", settled.Phase); Assert.Equal(995m, settled.Balance);
    }

    private sealed class NameReader : IGameWalletAccountReader
    {
        public bool Available = true; public int Calls;
        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken token)
        { Calls++; return Task.FromResult(new GameWalletAccountState(0, PlayerName: Available ? "Alice Trusted" : null)); }
    }
    private static FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-roulette-persistence-" + Guid.NewGuid().ToString("N"), Endpoint = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") ?? "127.0.0.1:8085",
        ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None
    }.Build();
}
