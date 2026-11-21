using System.Net;
using System.Text.Json;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.PackagedGame.Server;
using FortuneForge.Games.Roulette;
using FortuneForge.Server.Games.Roulette;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;

namespace FortuneForge.Games.Roulette.Tests;

public sealed class RouletteManagedLeaseTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RestartConfirmsOwnershipAndLeaseLossRetiresOnlyManagedOpenWagers()
    {
        var db = Database(); var seeded = await SeedAsync(db); var roster = new Roster();
        FirestoreRouletteService Restart() => new(db, new SignedRoulettePopulation(roster, new NoArrivals()), draw: () => 1);
        var renewed = await Restart().GetAsync("alice", seeded.RoundId);
        Assert.Equal(2, renewed.Players.Count); Assert.Single(renewed.Bets); Assert.Equal(995m, renewed.Balance);
        Assert.True(roster.Renewals > 0); Assert.Equal(0, roster.LegacyHeartbeats);

        roster.OwnsLease = false;
        var recovered = await Restart().GetAsync("alice", seeded.RoundId);
        Assert.True(Assert.Single(recovered.Players).IsCurrentPlayer);
        Assert.Single(recovered.Bets); Assert.Equal(995m, recovered.Balance); Assert.True(recovered.Revision > renewed.Revision);
        Assert.Contains("managed-a", roster.Released);
        var saved = await CheckpointAsync(db, seeded.RoundId);
        Assert.Equal("alice", Assert.Single(saved.State.Bets).PlayerId);
        Assert.Equal(1000m, saved.Balances["managed-a"]);

        var replay = await Restart().PlaceBetAsync("alice", seeded.RoundId, new("red", 5m, null, []), "roulette_lease_human_bet_0001", seeded.Revision - 1);
        Assert.Equal(JsonSerializer.Serialize(seeded, Json), JsonSerializer.Serialize(replay, Json));
        var settled = await Restart().SpinAsync("alice", seeded.RoundId, "roulette_lease_human_spin_0001", recovered.Revision);
        Assert.Equal("alice", Assert.Single(settled.Settlements).PlayerId); Assert.Equal(1005m, settled.Balance);
        Assert.Equal(JsonSerializer.Serialize(settled, Json), JsonSerializer.Serialize(await Restart().HistoryAsync("alice", seeded.RoundId, 1), Json));
        Assert.Empty((await db.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData("transport")]
    [InlineData("malformed")]
    public async Task UncertainSignedRenewalReturns503WithoutChangingHumanOrManagedState(string failure)
    {
        var db = Database(); var seeded = await SeedAsync(db); var roster = new Roster { Failure = failure };
        var checkpoint = db.Collection("rouletteTables").Document(seeded.RoundId.ToString("N"));
        var before = (await checkpoint.GetSnapshotAsync()).GetValue<string>("stateJson");
        var key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(key);
        builder.Configuration["Features:RouletteEnabled"] = "true";
        builder.Services.AddSingleton(db); GameWorkerModule.Configure(builder);
        builder.Services.AddSingleton<IRoulettePopulation>(new SignedRoulettePopulation(roster, new NoArrivals()));
        builder.Services.Remove(builder.Services.Single(item => item.ImplementationType == typeof(RoulettePackageWorker)));
        await using var app = builder.Build(); app.UseRouting(); app.UseGameWorkerAuthentication("roulette");
        GameWorkerModule.Map(app, GameWorkerAuthentication.GetAccountId); await app.StartAsync(); var client = app.GetTestClient();
        var path = $"/api/games/roulette/rounds/{seeded.RoundId:D}";
        HttpRequestMessage Request()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Add(GameWorkerAuthentication.AssertionHeader,
                GameWorkerAuthentication.CreateAssertion("roulette", "alice", "GET", path, [], key, DateTimeOffset.UtcNow));
            return request;
        }
        var unavailable = await client.SendAsync(Request());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Contains("roulette-retry-required", await unavailable.Content.ReadAsStringAsync());
        Assert.Equal(before, (await checkpoint.GetSnapshotAsync()).GetValue<string>("stateJson"));
        Assert.Empty(roster.Released); Assert.Equal(0, roster.LegacyHeartbeats);
        roster.Failure = null;
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Request())).StatusCode);
        var restored = await CheckpointAsync(db, seeded.RoundId);
        Assert.Equal(2, restored.Players.Count); Assert.Equal(2, restored.State.Bets.Length);
        Assert.Equal(995m, restored.Balances["alice"]);
    }

    [Fact]
    public async Task ConfirmedLossAfterSettlementKeepsTheOriginalPrivateHistory()
    {
        var db = Database(); var seeded = await SeedAsync(db); var roster = new Roster();
        FirestoreRouletteService Restart() => new(db, new SignedRoulettePopulation(roster, new NoArrivals()), draw: () => 1);
        var settled = await Restart().SpinAsync("alice", seeded.RoundId, "roulette_lease_history_spin_0001", seeded.Revision);
        Assert.Equal(2, settled.Players.Count);
        roster.OwnsLease = false;
        var current = await Restart().GetAsync("alice", seeded.RoundId);
        Assert.Single(current.Players);
        var historical = await Restart().HistoryAsync("alice", seeded.RoundId, settled.RoundNumber);
        Assert.Equal(JsonSerializer.Serialize(settled, Json), JsonSerializer.Serialize(historical, Json));
        Assert.Single((await db.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection("gameAnalyticsSettlementOutbox").GetSnapshotAsync()).Documents);
    }

    private static async Task<RouletteRoundResponse> SeedAsync(FirestoreDb db)
    {
        var service = new FirestoreRouletteService(db, new NoRoulettePopulation());
        var opened = await service.StartAsync("alice", "Alice", "roulette_lease_human_open_0001");
        var human = await service.PlaceBetAsync("alice", opened.RoundId, new("red", 5m, null, []), "roulette_lease_human_bet_0001", opened.Revision);
        var table = await CheckpointAsync(db, opened.RoundId);
        table.JoinManaged(new("managed-a", "Managed Player"), int.MaxValue, DateTime.UtcNow);
        table.State = RouletteEngine.PlaceBet(table.State, new("managed-a", RouletteBetKind.Black, 5m, null, []));
        table.Balances["managed-a"] -= 5m;
        RouletteGameService.ValidateTable(table);
        await db.Collection("rouletteTables").Document(opened.RoundId.ToString("N")).UpdateAsync("stateJson", JsonSerializer.Serialize(table, Json));
        return human;
    }

    private static async Task<RouletteGameService.RouletteTable> CheckpointAsync(FirestoreDb db, Guid id) =>
        JsonSerializer.Deserialize<RouletteGameService.RouletteTable>((await db.Collection("rouletteTables").Document(id.ToString("N")).GetSnapshotAsync()).GetValue<string>("stateJson"), Json)!;

    private static FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = "demo-roulette-leases-" + Guid.NewGuid().ToString("N"), Endpoint = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") ?? "127.0.0.1:8085",
        ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None
    }.Build();

    private sealed class NoArrivals : IManagedTablePopulationDirector
    {
        public ManagedTablePopulationDecision Observe(ManagedTablePopulationObservation value) => new(value.TableOpen && value.HumanPlayers + value.ManagedPlayers > 0, 0, value.HumanPlayers == 0);
        public void Close(string gameId, string tableId) { }
    }

    private sealed class Roster : IManagedPlayerRosterClient
    {
        public bool OwnsLease = true; public string? Failure; public int Renewals; public int LegacyHeartbeats;
        public List<string> Released { get; } = [];
        public Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveAsync(string assignmentId, int count, IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ManagedPlayerRosterProfile>>([]);
        public Task<bool> TryRenewAsync(string assignmentId, IReadOnlyCollection<string> ids, DateTime nowUtc, CancellationToken token)
        {
            Renewals++;
            return Failure is null ? Task.FromResult(OwnsLease) : Task.FromException<bool>(new ManagedPlayerRosterUnavailableException(
                Failure == "transport" ? new HttpRequestException("Lost signed roster reply.") : new JsonException("Malformed signed roster reply.")));
        }
        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> ids, DateTime nowUtc, CancellationToken token)
        { LegacyHeartbeats++; return Task.CompletedTask; }
        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> ids, DateTime nowUtc, CancellationToken token)
        { Released.AddRange(ids); return Task.CompletedTask; }
    }
}
