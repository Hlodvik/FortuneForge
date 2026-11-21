using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.ThreeCardPoker.Server;
using FortuneForge.Server.Cards.ThreeCardPoker.Table;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Games.ThreeCardPoker.Tests;

public sealed class ThreeCardPokerHttpTests
{
    [ThreeCardPokerEmulatorFact]
    public async Task ExistingCardRoutesUseTrustedNamesPreserveDeleteBodiesAndKeepRecoveryAvailableAfterAdmissionPause()
    {
        var database = new FirestoreDbBuilder { ProjectId = $"three-card-http-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        var wallet = new ThreeCardPokerTestWallet(database);
        await wallet.SetBalanceAsync("alice", 10_000);
        var roster = new ThreeCardPokerTestRoster();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["Features:ThreeCardPokerTableEnabled"] = "true";
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<IGameWalletClient>(wallet);
        builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
        builder.Services.AddSingleton<IManagedPlayerRosterClient>(roster);
        builder.Services.AddSingleton<IManagedTablePopulationDirector, ManagedTablePopulationDirector>();
        builder.Services.AddSingleton<FirestoreThreeCardPokerTableStore>();
        builder.Services.AddSingleton<IThreeCardPokerTableStore>(provider => provider.GetRequiredService<FirestoreThreeCardPokerTableStore>());
        builder.Services.AddSingleton<ThreeCardPokerTableService>();
        await using var app = builder.Build();
        app.MapThreeCardPoker(context => context.Request.Headers["X-Test-Account"].FirstOrDefault());
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var http = new HttpClient { BaseAddress = new Uri(address) };
        const string prefix = ThreeCardPokerBackend.PublicPath;
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(prefix + "/status")).StatusCode);
        http.DefaultRequestHeaders.Add("X-Test-Account", "alice");
        var joined = await Post<ThreeCardPokerTableMutationResponse>(http, prefix + "/queue", new JoinThreeCardPokerTableQueueRequest(0), "three_card_http_join_0001");
        var table = Assert.IsType<ThreeCardPokerTablePlaySessionResponse>(joined.Session);
        Assert.Equal("Trusted Alice", Assert.Single(table.Table.Seats).DisplayName);
        wallet.PlayerName = null;
        builder.Configuration["Features:ThreeCardPokerTableEnabled"] = "false";
        var paused = await http.GetFromJsonAsync<ThreeCardPokerTableStatusResponse>(prefix + "/status");
        Assert.False(paused!.Available);
        var accountReads = wallet.AccountReads;
        var repeated = await Post<ThreeCardPokerTableMutationResponse>(http, prefix + "/queue", new JoinThreeCardPokerTableQueueRequest(0), "three_card_http_join_0001");
        Assert.Equal(JsonSerializer.Serialize(joined), JsonSerializer.Serialize(repeated));
        Assert.Equal(accountReads, wallet.AccountReads);
        wallet.PlayerName = "Renamed Alice";
        using var fresh = Request(prefix + "/tables/" + table.Table.TableId + "/wagers", new ThreeCardPokerTableWagerRequest(1, 0, table.Version), "three_card_paused_wager_0001");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.SendAsync(fresh)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(prefix + "/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(prefix + "/snapshot")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(prefix + "/history")).StatusCode);
        using var cancel = new HttpRequestMessage(HttpMethod.Delete, prefix + "/queue/" + new string('a', 64))
        { Content = JsonContent.Create(new ThreeCardPokerTableVersionRequest(1)) };
        cancel.Headers.Add("Idempotency-Key", "three_card_cancel_body_0001");
        Assert.Equal(HttpStatusCode.NotFound, (await http.SendAsync(cancel)).StatusCode);
        await Post<ThreeCardPokerTableMutationResponse>(http, prefix + "/tables/" + table.Table.TableId + "/leave",
            new ThreeCardPokerTableVersionRequest(table.Version), "three_card_http_leave_0001");
        Assert.Equal(0, wallet.Calls);

        builder.Configuration["Features:ThreeCardPokerTableEnabled"] = "true";
        wallet.PlayerName = null;
        using var absentName = Request(prefix + "/queue", new JoinThreeCardPokerTableQueueRequest(0), "three_card_no_profile_0001");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await http.SendAsync(absentName)).StatusCode);
        await app.StopAsync();
    }

    private static async Task<T> Post<T>(HttpClient http, string path, object body, string key)
    {
        using var request = Request(path, body, key);
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
    private static HttpRequestMessage Request(string path, object body, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }
}
