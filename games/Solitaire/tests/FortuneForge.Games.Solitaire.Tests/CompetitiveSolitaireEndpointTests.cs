using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Cards.Solitaire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Games.Solitaire.Tests;

public sealed class CompetitiveSolitaireEndpointTests
{
    [SolitaireEmulatorFact]
    public async Task SignedLegacySurfaceUsesTrustedNamesRejectsMissingProfileScopeAndKeepsRefundRetriesExact()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var db = CompetitiveSolitaireBackendTests.Database();
        var wallet = new SolitaireTestWallet { IncludePlayerName = false };
        var roster = new SolitaireTestRoster();
        var store = CompetitiveSolitaireBackendTests.Store(db, wallet, roster);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(key);
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(new CompetitiveSolitaireService(store));
        builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
        await using var app = builder.Build();
        app.UseGameWorkerAuthentication(SolitaireModule.GameId);
        app.MapCompetitiveSolitaire(GameWorkerAuthentication.GetAccountId);
        await app.StartAsync();
        using var http = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/solitaire/session")).StatusCode);
        var join = new { playerCount = 4, buyInCredits = 5, idempotencyKey = "solitaire-http-entry-0001", drawCount = 3, playerName = "Untrusted Name" };
        using var missingName = await SendAsync(HttpMethod.Post, "/queue", join);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, missingName.StatusCode);
        Assert.Equal(0, wallet.SettlementCount);
        wallet.IncludePlayerName = true;
        using var entry = await SendAsync(HttpMethod.Post, "/queue", join);
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        var json = await entry.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        Assert.Equal("match", body.RootElement.GetProperty("session").GetProperty("kind").GetString());
        Assert.Contains("Alice", json);
        Assert.DoesNotContain("Untrusted Name", json);
        Assert.DoesNotContain("\"seed\"", json);
        wallet.IncludePlayerName = false;
        using var exact = await SendAsync(HttpMethod.Post, "/queue", join);
        Assert.Equal(json, await exact.Content.ReadAsStringAsync());
        Assert.Equal(1, wallet.SettlementCount);

        var ticket = await CompetitiveSolitaireBackendTests.SeedLegacyQueue(db, "bob", "solitaire-http-legacy-0001");
        wallet.Fund("bob", 9_500);
        const string refundKey = "solitaire-http-refund-0001";
        using var refund = await SendAsync(HttpMethod.Delete, "/queue/" + ticket, idempotencyKey: refundKey, account: "bob");
        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
        using var retried = await SendAsync(HttpMethod.Delete, "/queue/" + ticket, idempotencyKey: refundKey, account: "bob");
        Assert.Equal(await refund.Content.ReadAsStringAsync(), await retried.Content.ReadAsStringAsync());
        Assert.Equal(10_000, wallet.Balance("bob"));
        Assert.Equal(2, wallet.SettlementCount);
        using var bobHistory = await SendAsync(HttpMethod.Get, "/history-page?limit=30", account: "bob");
        using var history = JsonDocument.Parse(await bobHistory.Content.ReadAsStringAsync());
        Assert.Equal(0, history.RootElement.GetProperty("items").GetArrayLength());

        async Task<HttpResponseMessage> SendAsync(HttpMethod method, string suffix, object? payload = null,
            string? idempotencyKey = null, string account = "alice")
        {
            var path = SolitaireModule.ApiBasePath + suffix;
            var bytes = payload is null ? [] : JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var headers = idempotencyKey is null ? null : new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey };
            var observation = new GameRequestObservation(Guid.NewGuid().ToString("N"), account, null, null,
                DateTime.UtcNow, method.Method, path.Split('?')[0], SolitaireModule.GameId, null, idempotencyKey,
                null, null, "sanitized", "direct");
            using var request = new HttpRequestMessage(method, path);
            if (bytes.Length > 0)
            {
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.ContentType = new("application/json");
            }
            if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
            request.Headers.Add(GameWorkerAuthentication.AssertionHeader, GameWorkerAuthentication.CreateAssertion(
                SolitaireModule.GameId, account, method.Method, path, bytes, key, DateTimeOffset.UtcNow, observation, headers));
            return await http.SendAsync(request);
        }
    }
}
