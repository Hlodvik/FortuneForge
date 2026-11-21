using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Cards.CasinoWar;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Games.CasinoWar.Tests;

public sealed class CasinoWarEndpointTests
{
    [CasinoWarEmulatorFact]
    public async Task SignedPublicRoutesRecoverAnAmbiguousWalletResultAndProtectOwnerHistory()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var database = CasinoWarBackendTests.Database();
        var wallet = new CasinoWarTestWallet { LoseNextResponse = true };
        var store = new CasinoWarFirestoreStore(database, wallet, wallet, true);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(key);
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton(new CasinoWarService(store, TimeProvider.System, CasinoWarBackendTests.OpeningWinDeck));
        await using var app = builder.Build();
        app.UseGameWorkerAuthentication(CasinoWarRouteContract.GameId);
        app.MapCasinoWar(GameWorkerAuthentication.GetAccountId, true);
        await app.StartAsync();
        using var http = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(CasinoWarRouteContract.PathPrefix + "/status")).StatusCode);
        const string operation = "casino-war-signed-opening-0001";
        var terms = new CreateCasinoWarRoundRequest(1m, 0m);
        using var lost = await SendAsync(HttpMethod.Post, "/rounds", terms, operation);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        Assert.Equal("casino-war-unavailable", (await lost.Content.ReadFromJsonAsync<CasinoWarErrorResponse>())!.Code);
        using var recovered = await SendAsync(HttpMethod.Get, "/rounds/pending");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        var recovery = (await recovered.Content.ReadFromJsonAsync<CasinoWarRecoveryResponse>())!;
        Assert.NotNull(recovery.RecoveredRound);
        Assert.Equal("completed", recovery.RecoveredRound!.Phase);
        Assert.Empty(recovery.OpenRounds);
        using var retry = await SendAsync(HttpMethod.Post, "/rounds", terms, operation);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(recovery.RecoveredRound, await retry.Content.ReadFromJsonAsync<CasinoWarRoundResponse>());
        Assert.Equal(1, wallet.SettlementCount);
        using var history = await SendAsync(HttpMethod.Get, "/rounds/history?limit=20");
        Assert.Equal(recovery.RecoveredRound.RoundId,
            Assert.Single((await history.Content.ReadFromJsonAsync<CasinoWarRoundResponse[]>())!).RoundId);
        using var foreign = await SendAsync(HttpMethod.Get, "/rounds/" + recovery.RecoveredRound.RoundId, account: "bob");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        async Task<HttpResponseMessage> SendAsync(HttpMethod method, string suffix, object? payload = null,
            string? idempotencyKey = null, string account = "alice")
        {
            var path = CasinoWarRouteContract.PathPrefix + suffix;
            var body = payload is null ? [] : JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var headers = idempotencyKey is null ? null : new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey };
            var observation = new GameRequestObservation(Guid.NewGuid().ToString("N"), account, null, null,
                DateTime.UtcNow, method.Method, path.Split('?')[0], CasinoWarRouteContract.GameId, null,
                idempotencyKey, null, null, "sanitized", "direct");
            using var request = new HttpRequestMessage(method, path);
            if (body.Length > 0)
            {
                request.Content = new ByteArrayContent(body);
                request.Content.Headers.ContentType = new("application/json");
            }
            if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
            request.Headers.Add(GameWorkerAuthentication.AssertionHeader, GameWorkerAuthentication.CreateAssertion(
                CasinoWarRouteContract.GameId, account, method.Method, path, body, key,
                DateTimeOffset.UtcNow, observation, headers));
            return await http.SendAsync(request);
        }
    }
}
