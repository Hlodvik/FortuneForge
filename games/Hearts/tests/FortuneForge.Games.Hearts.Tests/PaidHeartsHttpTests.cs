using System.Net;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Hearts;

[Collection("Blackjack table Firestore emulator")]
public sealed class PaidHeartsHttpTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task Paid_routes_require_authentication_and_use_trusted_player_name()
    {
        await using var host = await Open();
        using var anonymous = await Send(host, "/join", "{\"entryCredits\":1}", "join", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var joined = await Send(host, "/join", "{\"entryCredits\":1,\"playerName\":\"Untrusted\"}", "join");
        Assert.Equal(HttpStatusCode.OK, joined.StatusCode);
        var response = JsonSerializer.Deserialize<PaidHeartsSessionResponse>(await joined.Content.ReadAsStringAsync(), Json)!;
        Assert.Equal("Display alice", Assert.Single(response.Queue!.Players).DisplayName);
        using var other = new HttpRequestMessage(HttpMethod.Get, PaidHeartsEndpoints.Prefix + "/matches/" + response.Queue.MatchId);
        other.Headers.Add("X-Test-Account", "stranger");
        using var stolen = await host.Client.SendAsync(other);
        Assert.Equal(HttpStatusCode.NotFound, stolen.StatusCode);
        await host.Store.PlayHouseAsync("alice", response.Queue.MatchId, response.Version, "house", DateTime.UtcNow.AddSeconds(5), default);
        var reference = host.Database.Collection("heartsPaidMatches").Document(response.Queue.MatchId);
        var match = JsonSerializer.Deserialize<PaidHeartsMatch>((await reference.GetSnapshotAsync()).GetValue<string>("matchJson"), Json)!;
        match.Players.First(value => !value.IsAccountBacked).ManagedAssignmentId = "imported-lease";
        match.LeaseRenewalAtUtc = DateTime.UtcNow.AddMinutes(-1);
        var privateJson = JsonSerializer.Serialize(match, Json);
        await reference.UpdateAsync("matchJson", privateJson);
        using var unauthorizedAdvance = await Send(host, "/matches/" + match.Id + "/advance",
            JsonSerializer.Serialize(new PaidHeartsVersionRequest(match.Version), Json), "stranger-advance", "stranger");
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedAdvance.StatusCode);
        Assert.Equal(privateJson, (await reference.GetSnapshotAsync()).GetValue<string>("matchJson"));
    }
    [Fact]
    public async Task Cached_paid_join_works_after_pause_and_profile_outage_and_changed_terms_conflict()
    {
        await using var host = await Open();
        using var first = await Send(host, "/join", "{\"entryCredits\":1}", "join");
        var original = await first.Content.ReadAsStringAsync();
        host.Configuration[HeartsBackend.FreeAdmissionSetting] = "false";
        host.Bank.FailProfileReads = true;
        using var replay = await Send(host, "/join", "{\"entryCredits\":1}", "join");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(original, await replay.Content.ReadAsStringAsync());
        using var changed = await Send(host, "/join", "{\"entryCredits\":2}", "join");
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var paused = await Send(host, "/join", "{\"entryCredits\":1}", "new-join");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, paused.StatusCode);
    }
    [Fact]
    public async Task Paid_advance_has_immutable_reply_and_body_terms_across_later_table_mutations()
    {
        await using var host = await Open();
        using var first = await Send(host, "/join", "{\"entryCredits\":1}", "join");
        var queue = JsonSerializer.Deserialize<PaidHeartsSessionResponse>(await first.Content.ReadAsStringAsync(), Json)!;
        var path = "/matches/" + queue.Queue!.MatchId + "/advance";
        var body = JsonSerializer.Serialize(new PaidHeartsVersionRequest(queue.Version), Json);
        using var advance = await Send(host, path, body, "advance");
        Assert.Equal(HttpStatusCode.OK, advance.StatusCode);
        var original = await advance.Content.ReadAsStringAsync();
        await Seed(host.Database, "bruno");
        await host.Store.JoinAsync("bruno", "Bruno", 1m, "bruno-join", DateTime.UtcNow, default);
        host.Configuration[HeartsBackend.FreeAdmissionSetting] = "false";
        using var replay = await Send(host, path, body, "advance");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(original, await replay.Content.ReadAsStringAsync());
        using var changed = await Send(host, path, JsonSerializer.Serialize(new PaidHeartsVersionRequest(queue.Version + 1), Json), "advance");
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
    }
    private async Task<Harness> Open()
    {
        var database = new FirestoreDbBuilder { ProjectId = "demo-hearts-paid-http-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
            ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None }.Build();
        await Seed(database, "alice");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [HeartsBackend.FreeAdmissionSetting] = "true" });
        var bank = new PaidHeartsTestBank(database);
        var store = PaidHeartsTestStore.Create(database, new NoRoster(), bank: bank,
            admits: () => builder.Configuration.GetValue<bool>(HeartsBackend.FreeAdmissionSetting));
        builder.Services.AddSingleton(store);
        var app = builder.Build();
        app.UseLocalGameWorkerCommandDigests(_ => true);
        app.MapHeartsPaid(context => context.Request.Headers["X-Test-Account"].FirstOrDefault());
        await app.StartAsync();
        return new(app, app.GetTestClient(), database, builder.Configuration, bank, store);
    }
    private static Task Seed(FirestoreDb db, string user) => db.Collection("userBalances").Document(user + "_slotsCredits").SetAsync(new Dictionary<string, object>
    { ["available"] = 100L, ["availableFractionalCents"] = 0L });
    private static Task<HttpResponseMessage> Send(Harness host, string path, string body, string key, string? user = "alice")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PaidHeartsEndpoints.Prefix + path)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("Idempotency-Key", key);
        if (user is not null) request.Headers.Add("X-Test-Account", user);
        return host.Client.SendAsync(request);
    }
    private sealed record Harness(WebApplication App, HttpClient Client, FirestoreDb Database, IConfiguration Configuration,
        PaidHeartsTestBank Bank, FirestorePaidHeartsStore Store) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() { Client.Dispose(); await App.DisposeAsync(); }
    }
    private sealed class NoRoster : IManagedPlayerRosterClient
    {
        public Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveAsync(string assignment, int count, IReadOnlyCollection<string> excluded,
            DateTime now, CancellationToken token) => throw new InvalidOperationException("Paid Hearts only admits labelled virtual house seats.");
        public Task HeartbeatAsync(string assignment, IReadOnlyCollection<string> profiles, DateTime now, CancellationToken token) => Task.CompletedTask;
        public Task ReleaseAsync(string assignment, IReadOnlyCollection<string> profiles, DateTime now, CancellationToken token) => Task.CompletedTask;
    }
}
