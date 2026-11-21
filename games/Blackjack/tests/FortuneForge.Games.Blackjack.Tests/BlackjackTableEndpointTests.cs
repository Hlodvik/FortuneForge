using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Cards.Blackjack.Table;
using FortuneForge.Server.Tests.Blackjack.Table;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Games.Blackjack.Tests;

[Collection("Blackjack table Firestore emulator")]
public sealed class BlackjackTableEndpointTests(BlackjackTableFirestoreEmulatorFixture fixture)
{
    [Fact]
    public async Task Signed_join_recovers_original_terms_before_profile_reads_and_rejects_invalid_new_names()
    {
        var db = new FirestoreDbBuilder
        {
            ProjectId = "demo-blackjack-http-" + Guid.NewGuid().ToString("N"), Endpoint = fixture.Endpoint,
            ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure, EmulatorDetection = Google.Api.Gax.EmulatorDetection.None,
        }.Build();
        var signingKey = RandomNumberGenerator.GetBytes(32);
        var wallet = new Wallet();
        var store = new FirestoreBlackjackTableStore(db, wallet, wallet, new NoRoster(), multiplayerEnabled: false);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(signingKey);
        builder.Services.AddSingleton<IBlackjackTableStore>(store);
        builder.Services.AddSingleton(new BlackjackTableService(store));
        builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
        await using var app = builder.Build();
        app.UseGameWorkerAuthentication("blackjack");
        app.MapBlackjackTable(GameWorkerAuthentication.GetAccountId);
        await app.StartAsync();
        using var http = app.GetTestClient();
        foreach (var name in new string?[] { null, " Alice ", "Ali\nce" })
        {
            wallet.Name = name;
            using var denied = await Join("alice", "blackjack-http-join-0001");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
            Assert.Equal(0, wallet.Settlements);
        }
        wallet.Name = "Alice";
        using var accepted = await Join("alice", "blackjack-http-join-0001");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var original = await accepted.Content.ReadAsStringAsync();
        wallet.ReadsUnavailable = true;
        using var exact = await Join("alice", "blackjack-http-join-0001");
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
        Assert.Equal(original, await exact.Content.ReadAsStringAsync());
        using var changed = await Join("alice", "blackjack-http-join-0001", expectedVersion: 1);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var newEntry = await Join("alice", "blackjack-http-join-0002");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, newEntry.StatusCode);
        wallet.ReadsUnavailable = false;
        wallet.LoseNextReply = true;
        using var lost = await Join("bob", "blackjack-http-lost-0001");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, lost.StatusCode);
        wallet.ReadsUnavailable = true;
        using var recovered = await Join("bob", "blackjack-http-lost-0001");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(2, wallet.Settlements);

        async Task<HttpResponseMessage> Join(string account, string idempotencyKey, int expectedVersion = 0)
        {
            const string path = "/api/cards/blackjack/table/queue";
            var body = JsonSerializer.SerializeToUtf8Bytes(new { expectedVersion });
            var headers = new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey };
            var observation = new GameRequestObservation(Guid.NewGuid().ToString("N"), account, null, null,
                DateTime.UtcNow, "POST", path, "blackjack", null, idempotencyKey, null, null, "sanitized", "direct");
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(body) };
            request.Content.Headers.ContentType = new("application/json");
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            request.Headers.Add(GameWorkerAuthentication.AssertionHeader, GameWorkerAuthentication.CreateAssertion(
                "blackjack", account, "POST", path, body, signingKey, DateTimeOffset.UtcNow, observation, headers));
            return await http.SendAsync(request);
        }
    }

    private sealed class Wallet : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly Dictionary<string, GameWalletSettlementReceipt> receipts = [];
        internal string? Name { get; set; }
        internal bool ReadsUnavailable { get; set; }
        internal bool LoseNextReply { get; set; }
        internal int Settlements => receipts.Count;
        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken token) => ReadsUnavailable
            ? Task.FromException<GameWalletAccountState>(new GameWalletUnavailableException())
            : Task.FromResult(new GameWalletAccountState(0, 10000, Name));
        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken token)
        {
            if (receipts.TryGetValue(request.OperationId, out var saved)) return Task.FromResult(saved with { WasAlreadySettled = true });
            Assert.Equal(0, request.WagerCents); Assert.Equal(0, request.PayoutCents);
            var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId("blackjack", accountId, request.OperationId),
                null, null, 10000, 10000, 10000, DateTimeOffset.UtcNow, false, false);
            receipts.Add(request.OperationId, receipt);
            if (LoseNextReply) { LoseNextReply = false; throw new GameWalletUnavailableException(); }
            return Task.FromResult(receipt);
        }
    }

    private sealed class NoRoster : IManagedPlayerRosterClient
    {
        public Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveAsync(string assignmentId, int count,
            IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken token) => throw new InvalidOperationException();
        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) => throw new InvalidOperationException();
        public Task<bool> TryRenewAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) => throw new InvalidOperationException();
        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc, CancellationToken token) => throw new InvalidOperationException();
    }
}
