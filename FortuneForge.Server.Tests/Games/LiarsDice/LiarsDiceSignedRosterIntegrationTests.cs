using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.LiarsDice;
using FortuneForge.Games.LiarsDice.Server;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using DurableLiarsDiceService = FortuneForge.Games.LiarsDice.Server.LiarsDiceGameService;

namespace FortuneForge.Server.Tests.Games.LiarsDice;

[Collection("Blackjack table Firestore emulator")]
public sealed class LiarsDiceSignedRosterIntegrationTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private const string Game = "liars-dice";
    private const string Human = "signed-liars-human";
    private const string Starts = "/api/games/liars-dice/matches";

    [Fact]
    public async Task SignedWorkerRetriesOneDurableMatchRenewsRealLeasesAndReleasesThemOnCompletion()
    {
        var database = Database();
        await SeedHumanAsync(database);
        var clock = new Clock();
        var rosterKey = RandomNumberGenerator.GetBytes(32);
        var workerKey = RandomNumberGenerator.GetBytes(32);
        await using var platform = await StartPlatformAsync(database, clock, rosterKey);
        await using var worker = await StartWorkerAsync(database, clock, workerKey, platform, rosterKey);
        using var client = Client(worker);

        using var unsigned = await client.PostAsJsonAsync(Starts, new { seed = 77, dicePerPlayer = 1 });
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        Assert.Empty((await database.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);

        const string body = "{ \"seed\": 77, \"dicePerPlayer\": 1 }";
        using var started = await SendWorkerAsync(client, workerKey, clock, HttpMethod.Post, Starts, body, "start");
        var match = await ReadMatchAsync(started);
        Assert.True(PuzzleSessionIdentity.IsDurableId(match.MatchId));
        var assignment = LiarsDiceSession.AssignmentId(match.MatchId);
        var ids = match.Players.Where(player => !player.IsHuman).Select(player => player.Id).Order().ToArray();
        Assert.Equal(3, ids.Length);
        Assert.DoesNotContain(Human, ids);
        Assert.Equal(3, (await database.Collection("managedPlayerAssignments").GetSnapshotAsync()).Count);
        foreach (var id in ids)
        {
            var profile = await database.Collection("users").Document(id).GetSnapshotAsync();
            Assert.True(FirestoreManagedPlayerProfileRepository.IsManagedPlayer(profile));
            Assert.Equal(match.Players.Single(player => player.Id == id).DisplayName, profile.GetValue<string>("playerName"));
            await AssertLeaseAsync(database, id, assignment, clock.GetUtcNow().UtcDateTime);
        }

        using var retried = await SendWorkerAsync(client, workerKey, clock, HttpMethod.Post, Starts, body, "start");
        Assert.Equal(JsonSerializer.Serialize(match), JsonSerializer.Serialize(await ReadMatchAsync(retried)));
        Assert.Single((await database.Collection("liarsDiceMatches").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("liarsDiceRequestReceipts").GetSnapshotAsync()).Documents);
        Assert.Equal(ids, (await database.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents.Select(item => item.Id).Order());

        // Equivalent bound JSON with different bytes must not reuse an accepted operation.
        using var changed = await SendWorkerAsync(client, workerKey, clock, HttpMethod.Post, Starts,
            "{\"seed\":77,\"dicePerPlayer\":1}", "start");
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        var path = $"{Starts}/{match.MatchId:D}";
        using var otherHuman = await SendWorkerAsync(client, workerKey, clock, HttpMethod.Get, path, player: "another-human");
        Assert.Equal(HttpStatusCode.NotFound, otherHuman.StatusCode);

        clock.Advance(TimeSpan.FromMinutes(1));
        using (var resumed = await SendWorkerAsync(client, workerKey, clock, HttpMethod.Get, path))
            match = await ReadMatchAsync(resumed);
        foreach (var id in ids)
            await AssertLeaseAsync(database, id, assignment, clock.GetUtcNow().UtcDateTime);

        for (var step = 0; step < 500 && match.Winner is null; step++)
        {
            clock.Advance(TimeSpan.FromSeconds(2));
            var action = match.Phase == "resolved" ? "/next-round"
                : match.CurrentPlayerId != LiarsDiceSession.HumanId ? "/advance"
                : match.CurrentBid is null ? "/bid" : "/challenge";
            var commandBody = action == "/next-round" ? JsonSerializer.Serialize(new { seed = step + 1 })
                : action == "/bid" ? "{\"quantity\":1,\"face\":1}" : "";
            using var next = await SendWorkerAsync(client, workerKey, clock, HttpMethod.Post,
                path + action, commandBody, $"turn-{step}");
            match = await ReadMatchAsync(next);
        }
        Assert.NotNull(match.Winner);
        Assert.Empty((await database.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);
        foreach (var id in ids)
            Assert.Equal(clock.GetUtcNow().UtcDateTime, (await database.Collection("users").Document(id)
                .GetSnapshotAsync()).GetValue<Timestamp>("lastActiveAt").ToDateTime());
        var saved = Assert.Single((await database.Collection("liarsDiceMatches").GetSnapshotAsync()).Documents);
        var session = JsonSerializer.Deserialize<LiarsDiceSession>(saved.GetValue<string>("json"), LiarsDiceStateJson.Options)!;
        Assert.Equal(Human, session.UserId);
        Assert.Empty(LiarsDiceSessionReplay.Check(session));
        Assert.Single((await database.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);

        // An old accepted start receipt must not reserve opponents again after its match finishes.
        using var completedStartRetry = await SendWorkerAsync(client, workerKey, clock, HttpMethod.Post, Starts, body, "start");
        Assert.Equal(match.MatchId, (await ReadMatchAsync(completedStartRetry)).MatchId);
        Assert.Empty((await database.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("liarsDiceMatches").GetSnapshotAsync()).Documents);
        Assert.False(FirestoreManagedPlayerProfileRepository.IsManagedPlayer(
            await database.Collection("users").Document(Human).GetSnapshotAsync()));
    }

    [Fact]
    public async Task SignedRosterCannotRenewHeartbeatOrReleaseAnotherGameOrAssignment()
    {
        var database = Database();
        var clock = new Clock();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var platform = await StartPlatformAsync(database, clock, key);
        using var diceServices = RosterServices(platform, clock, key, Game);
        using var otherServices = RosterServices(platform, clock, key, "blackjack");
        var dice = diceServices.GetRequiredService<IManagedPlayerRosterClient>();
        var other = otherServices.GetRequiredService<IManagedPlayerRosterClient>();
        const string assignment = "liars-dice:signed-scope";
        var players = await dice.ReserveAsync(assignment, 3, [Human], clock.GetUtcNow().UtcDateTime, default);
        var ids = players.Select(player => player.UserId).ToArray();
        Assert.Equal(3, ids.Length);
        var before = (await database.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents
            .ToDictionary(item => item.Id, item => item.UpdateTime);
        clock.Advance(TimeSpan.FromMinutes(1));
        foreach (var (client, claimedAssignment) in new[] { (other, assignment), (dice, assignment + "-other") })
        {
            Assert.False(await client.TryRenewAsync(claimedAssignment, ids, clock.GetUtcNow().UtcDateTime, default));
            await client.HeartbeatAsync(claimedAssignment, ids, clock.GetUtcNow().UtcDateTime, default);
            await client.ReleaseAsync(claimedAssignment, ids, clock.GetUtcNow().UtcDateTime, default);
        }

        using var http = Client(platform);
        var lease = JsonSerializer.SerializeToUtf8Bytes(new ManagedPlayerLeaseRequest(assignment, ids, clock.GetUtcNow().UtcDateTime),
            GameManagedPlayerRosterProtocol.WireJson);
        using var unsigned = await http.PostAsync(GameManagedPlayerRosterProtocol.Route(Game, "release"), Content(lease));
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        using var wrongGame = await SendRosterAsync(http, key, clock, "blackjack", Game, assignment, lease);
        Assert.Equal(HttpStatusCode.Forbidden, wrongGame.StatusCode);
        using var wrongLease = await SendRosterAsync(http, key, clock, Game, Game, assignment + "-other", lease);
        Assert.Equal(HttpStatusCode.BadRequest, wrongLease.StatusCode);
        foreach (var item in (await database.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents)
            Assert.Equal(before[item.Id], item.UpdateTime);
        Assert.Equal(3, (await database.Collection("managedPlayerAssignments").GetSnapshotAsync()).Count);

        Assert.True(await dice.TryRenewAsync(assignment, ids, clock.GetUtcNow().UtcDateTime, default));
        foreach (var id in ids) await AssertLeaseAsync(database, id, assignment, clock.GetUtcNow().UtcDateTime);
        await dice.ReleaseAsync(assignment, ids, clock.GetUtcNow().UtcDateTime, default);
        Assert.Empty((await database.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableSignedRosterCannotInventOpponentsOrCommitAStartReceipt(bool wrongSigningKey)
    {
        var database = Database();
        await SeedHumanAsync(database);
        var clock = new Clock();
        var rosterKey = RandomNumberGenerator.GetBytes(32);
        var workerKey = RandomNumberGenerator.GetBytes(32);
        await using var platform = await StartPlatformAsync(database, clock, rosterKey);
        platform.Configuration[$"ManagedPlayerRoster:Games:{Game}:Enabled"] = wrongSigningKey ? "true" : "false";
        await using var worker = await StartWorkerAsync(database, clock, workerKey, platform,
            wrongSigningKey ? RandomNumberGenerator.GetBytes(32) : rosterKey);
        using var client = Client(worker);
        const string body = "{\"seed\":77,\"dicePerPlayer\":1}";
        using var unavailable = await SendWorkerAsync(client, workerKey, clock, HttpMethod.Post, Starts, body, "start");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("liars-dice-worker-unavailable", (await unavailable.Content.ReadFromJsonAsync<LiarsDiceErrorResponse>())!.Code);
        Assert.Empty((await database.Collection("liarsDiceMatches").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("liarsDiceRequestReceipts").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("managedPlayerAssignments").GetSnapshotAsync()).Documents);
        Assert.Equal(Human, Assert.Single((await database.Collection("users").GetSnapshotAsync()).Documents).Id);

        platform.Configuration[$"ManagedPlayerRoster:Games:{Game}:Enabled"] = "true";
        worker.Configuration["ManagedPlayerRoster:SigningKey"] = Convert.ToBase64String(rosterKey);
        using var recovered = await SendWorkerAsync(client, workerKey, clock, HttpMethod.Post, Starts, body, "start");
        Assert.Equal(3, (await ReadMatchAsync(recovered)).Players.Count(player => !player.IsHuman));
        Assert.Single((await database.Collection("liarsDiceMatches").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("liarsDiceRequestReceipts").GetSnapshotAsync()).Documents);
    }

    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-signed-liars-{Guid.NewGuid():N}", Endpoint = fixture.Endpoint,
        ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
        EmulatorDetection = Google.Api.Gax.EmulatorDetection.None
    }.Build();

    private static Task SeedHumanAsync(FirestoreDb database) => database.Collection("users").Document(Human).SetAsync(
        new Dictionary<string, object> { ["userId"] = Human, ["playerName"] = "Human Player", ["authProvider"] = "firebase" });

    private static async Task<WebApplication> StartPlatformAsync(FirestoreDb database, Clock clock, byte[] key)
    {
        var builder = Builder();
        foreach (var game in new[] { Game, "blackjack" })
        {
            builder.Configuration[$"ManagedPlayerRoster:Games:{game}:Enabled"] = "true";
            builder.Configuration[$"ManagedPlayerRoster:Games:{game}:SigningKey"] = Convert.ToBase64String(key);
        }
        builder.Services.AddSingleton<TimeProvider>(clock);
        var profiles = new FirestoreManagedPlayerProfileRepository(database);
        builder.Services.AddSingleton<IManagedPlayerQueuer>(new ManagedPlayerQueuer(new ManagedPlayerProfileGenerator(profiles),
            profiles, new FirestoreManagedPlayerAssignmentStore(database)));
        var app = builder.Build();
        app.MapGameManagedPlayerRoster();
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartWorkerAsync(FirestoreDb database, Clock clock, byte[] workerKey,
        WebApplication platform, byte[] rosterKey)
    {
        var builder = Builder();
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(workerKey);
        builder.Configuration["ManagedPlayerRoster:Url"] = platform.Urls.Single();
        builder.Configuration["ManagedPlayerRoster:SigningKey"] = Convert.ToBase64String(rosterKey);
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddManagedPlayerRosterClient(Game);
        builder.Services.AddSingleton<ILiarsDiceManagedPlayerRoster, LiarsDiceWorkerRoster>();
        builder.Services.AddSingleton<DurableLiarsDiceService>();
        var app = builder.Build();
        app.UseRouting();
        app.UseGameWorkerAuthentication(Game);
        app.MapLiarsDice(GameWorkerAuthentication.GetAccountId, GameWorkerAuthentication.GetVerifiedBodySha256);
        await app.StartAsync();
        return app;
    }

    private static ServiceProvider RosterServices(WebApplication platform, Clock clock, byte[] key, string game)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ManagedPlayerRoster:Url"] = platform.Urls.Single(), ["ManagedPlayerRoster:SigningKey"] = Convert.ToBase64String(key)
        }).Build());
        services.AddSingleton<IHostEnvironment>(platform.Environment);
        services.AddSingleton<TimeProvider>(clock);
        services.AddManagedPlayerRosterClient(game);
        return services.BuildServiceProvider();
    }

    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        return builder;
    }

    private static HttpClient Client(WebApplication app) => new(new SocketsHttpHandler { UseProxy = false })
        { BaseAddress = new Uri(app.Urls.Single()) };

    private static async Task<HttpResponseMessage> SendWorkerAsync(HttpClient client, byte[] key, Clock clock,
        HttpMethod method, string path, string? body = null, string? command = null, string player = Human)
    {
        var bytes = Encoding.UTF8.GetBytes(body ?? "");
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = Content(bytes);
        Dictionary<string, string>? headers = command is null ? null : new() { ["Idempotency-Key"] = command };
        if (command is not null) request.Headers.Add("Idempotency-Key", command);
        var observation = method == HttpMethod.Post ? new GameRequestObservation(Guid.NewGuid().ToString("N"), player,
            null, null, clock.GetUtcNow().UtcDateTime, method.Method, path, Game, null, command, null, null, "omitted", "direct") : null;
        request.Headers.Add(GameWorkerAuthentication.AssertionHeader, GameWorkerAuthentication.CreateAssertion(Game, player,
            method.Method, path, bytes, key, DateTimeOffset.UtcNow, observation, headers));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendRosterAsync(HttpClient client, byte[] key, Clock clock,
        string routeGame, string assertedGame, string assertedAssignment, byte[] body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GameManagedPlayerRosterProtocol.Route(routeGame, "release"))
            { Content = Content(body) };
        request.Headers.Add(GameManagedPlayerRosterProtocol.AssertionHeader,
            GameManagedPlayerRosterProtocol.CreateAssertion(assertedGame, "release", assertedAssignment, body, key, clock.GetUtcNow()));
        return await client.SendAsync(request);
    }

    private static ByteArrayContent Content(byte[] body)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private static async Task<LiarsDiceMatchResponse> ReadMatchAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LiarsDiceMatchResponse>())!;
    }

    private static async Task AssertLeaseAsync(FirestoreDb database, string id, string assignment, DateTime heartbeat)
    {
        var lease = await database.Collection("managedPlayerAssignments").Document(id).GetSnapshotAsync();
        Assert.Equal(Game, lease.GetValue<string>("gameId"));
        Assert.Equal(assignment, lease.GetValue<string>("assignmentId"));
        Assert.Equal(heartbeat, lease.GetValue<Timestamp>("heartbeatAt").ToDateTime());
        Assert.Equal(heartbeat.AddMinutes(30), lease.GetValue<Timestamp>("leaseUntil").ToDateTime());
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }

}
