using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Server.Tests.Accounts;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class GameWalletBatchRpcTests(SolitaireFirestoreEmulatorFixture fixture)
{
    private const string GameId = "texas-holdem";
    private const string Actor = "alice";

    [Fact]
    public async Task Concurrent_exact_retries_post_all_net_effects_once_and_verify_the_original_ledger()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key);
        using var worker = Worker(app, key);
        var wallet = worker.GetRequiredService<IGameWalletBatchClient>();
        await Seed(database, "alice", 500);
        await Seed(database, "bob", 200);
        await Seed(database, "carol", 100);
        var request = Request(new("alice", 125, 0), new("bob", 75, 0), new("carol", 0, 200));
        var replies = await Task.WhenAll(wallet.SettleBatchAsync(Actor, request, default), wallet.SettleBatchAsync(Actor, request, default));
        var first = Assert.Single(replies, value => !value.WasAlreadySettled);
        var replay = Assert.Single(replies, value => value.WasAlreadySettled);
        Assert.Equal(first.SettledAtUtc, replay.SettledAtUtc);
        Assert.Equal(first.Accounts.ToArray(), replay.Accounts.ToArray());
        Assert.False(first.BatchDeclined);
        Assert.Equal(new long[] { 375, 125, 300 }, first.Accounts.Select(account => account.BalanceAfterCents));
        Assert.Equal(new long[] { 375, 125, 300 }, await Balances(database));
        Assert.Equal(3, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.Single((await database.Collection(GameWalletBatchFirestoreStore.ReceiptCollection).GetSnapshotAsync()).Documents);

        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleBatchAsync("bob", request, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleBatchAsync(Actor,
            request with { Effects = [request.Effects[0], request.Effects[1]] }, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleBatchAsync(Actor,
            request with { OutcomeSha256 = new string('b', 64) }, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleBatchAsync(Actor,
            request with { Effects = [request.Effects[0] with { MinimumBalanceBeforeCents = 1 }, request.Effects[1], request.Effects[2]] }, default));
        Assert.Equal(new long[] { 375, 125, 300 }, await Balances(database));

        var leg = database.Collection("balanceTransactions").Document(first.Accounts[0].WagerTransactionId!);
        await leg.UpdateAsync("amountCents", -124L);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => wallet.SettleBatchAsync(Actor, request, default));
        Assert.Equal(new long[] { 375, 125, 300 }, await Balances(database));
    }

    [Fact]
    public async Task A_short_account_declines_the_group_and_freezes_each_balance_without_a_debit_or_credit()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key);
        using var worker = Worker(app, key);
        var wallet = worker.GetRequiredService<IGameWalletBatchClient>();
        await Seed(database, "alice", 500);
        await Seed(database, "bob", 200);
        await Seed(database, "carol", 100);
        var request = Request(new("alice", 125, 0), new("bob", 30, 0, MinimumBalanceBeforeCents: 201), new("carol", 0, 100));
        var declined = await wallet.SettleBatchAsync(Actor, request, default);
        Assert.True(declined.BatchDeclined);
        Assert.Equal(new[] { false, true, false }, declined.Accounts.Select(account => account.InsufficientFunds));
        Assert.All(declined.Accounts, account =>
        {
            Assert.Equal(account.BalanceBeforeCents, account.BalanceAfterCents);
            Assert.Null(account.WagerTransactionId);
            Assert.Null(account.PayoutTransactionId);
        });
        Assert.Equal(new long[] { 500, 200, 100 }, await Balances(database));
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        await Seed(database, "bob", 201);
        var retry = await wallet.SettleBatchAsync(Actor, request, default);
        Assert.True(retry.WasAlreadySettled);
        Assert.True(retry.BatchDeclined);
        Assert.Equal(declined.Accounts.ToArray(), retry.Accounts.ToArray());
        Assert.Equal(new long[] { 500, 201, 100 }, await Balances(database));
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);

        var fresh = await wallet.SettleBatchAsync(Actor, request with { OperationId = Guid.NewGuid().ToString("N") }, default);
        Assert.False(fresh.BatchDeclined);
        Assert.Equal(new long[] { 375, 171, 200 }, await Balances(database));
    }

    [Fact]
    public async Task Batch_route_requires_dedicated_texas_scope_key_and_exact_unmodified_body()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        var ordinaryKey = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key, ordinaryKey);
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var request = Request(new("alice", 125, 0), new("bob", 75, 0));
        byte[] Encode(GameWalletBatchRequest value) => JsonSerializer.SerializeToUtf8Bytes(value, GameWalletAuthentication.WireJson);
        var bytes = Encode(request);
        var route = GameWalletBatchAuthentication.Route(GameId);
        var now = DateTimeOffset.UtcNow;
        var valid = GameWalletBatchAuthentication.CreateAssertion(GameId, Actor, bytes, key, now);

        await Status(HttpStatusCode.Forbidden, route, bytes, assertion: null);
        await Status(HttpStatusCode.Forbidden, route, bytes, GameWalletBatchAuthentication.CreateAssertion(GameId, Actor, bytes, ordinaryKey, now));
        await Status(HttpStatusCode.Forbidden, route, bytes, GameWorkerAuthentication.CreateAssertion(
            $"wallet:{GameId}", Actor, "POST", route, bytes, key, now));
        await Status(HttpStatusCode.Forbidden, route, Encode(request with { OutcomeSha256 = new string('f', 64) }), valid);
        await Status(HttpStatusCode.NotFound, GameWalletBatchAuthentication.Route("hi-lo"), bytes, valid);
        await Status(HttpStatusCode.NotFound, route + "?userId=carol", bytes, valid);
        var duplicate = Encode(request with { Effects = [request.Effects[0], request.Effects[0]] });
        await Status(HttpStatusCode.BadRequest, route, duplicate, GameWalletBatchAuthentication.CreateAssertion(GameId, Actor, duplicate, key, now));
        app.Configuration[$"GameWallet:Games:{GameId}:SigningKey"] = Convert.ToBase64String(key);
        await Status(HttpStatusCode.ServiceUnavailable, route, bytes, valid);
        Assert.Empty((await database.Collection(GameWalletBatchFirestoreStore.ReceiptCollection).GetSnapshotAsync()).Documents);

        async Task Status(HttpStatusCode expected, string path, byte[] content, string? assertion)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(content) };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            if (assertion is not null) message.Headers.Add(GameWalletAuthentication.AssertionHeader, assertion);
            using var response = await http.SendAsync(message);
            Assert.Equal(expected, response.StatusCode);
        }
    }

    [Fact]
    public async Task Hearts_batch_key_can_credit_other_accounts_but_can_only_debit_the_signed_actor()
    {
        const string hearts = "hearts";
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key, gameId: hearts);
        using var worker = Worker(app, key, hearts);
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var wallet = worker.GetRequiredService<IGameWalletBatchClient>();
        await Seed(database, Actor, 600);
        await Seed(database, "bob", 200);
        var debit = Request(new GameWalletBatchEffect(Actor, 100, 0));
        var award = Request(new GameWalletBatchEffect("bob", 0, 400));
        Assert.False((await wallet.SettleBatchAsync(Actor, debit, default)).BatchDeclined);
        var credited = await wallet.SettleBatchAsync(Actor, award, default);
        Assert.Equal(600, Assert.Single(credited.Accounts).BalanceAfterCents);
        Assert.True((await wallet.SettleBatchAsync(Actor, award, default)).WasAlreadySettled);

        // Exercise the platform permission even when the worker-side guard is bypassed.
        foreach (var disallowed in new[]
        {
            Request(new GameWalletBatchEffect("bob", 1, 0)),
            Request(new GameWalletBatchEffect(Actor, 100_001, 0)),
            Request(new(Actor, 0, 400_000), new("bob", 0, 1)),
            Request(new(Actor, 0, 0), new("bob", 0, 0)),
            Request(new GameWalletBatchEffect("bob", 0, 1, MinimumBalanceBeforeCents: 201)),
            Request(new(Actor, 0, 0), new("bob", 0, 1), new("carol", 0, 1), new("dora", 0, 1), new("erin", 0, 1))
        })
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(disallowed, GameWalletAuthentication.WireJson);
            using var message = new HttpRequestMessage(HttpMethod.Post, GameWalletBatchAuthentication.Route(hearts))
                { Content = new ByteArrayContent(bytes) };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            message.Headers.Add(GameWalletAuthentication.AssertionHeader,
                GameWalletBatchAuthentication.CreateAssertion(hearts, Actor, bytes, key, DateTimeOffset.UtcNow));
            using var response = await http.SendAsync(message);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Equal(2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.Equal(2, (await database.Collection(GameWalletBatchFirestoreStore.ReceiptCollection).GetSnapshotAsync()).Count);
    }

    private FirestoreDb Database()
    {
        _ = fixture;
        return new FirestoreDbBuilder
        {
            ProjectId = $"demo-wallet-batch-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
    }

    private static async Task<WebApplication> StartWallet(FirestoreDb database, byte[] key, byte[]? ordinaryKey = null,
        string gameId = GameId)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"GameWallet:Games:{gameId}:BatchSigningKey"] = Convert.ToBase64String(key),
            [$"GameWallet:Games:{gameId}:AllowMultiAccountBatch"] = "true",
            [$"GameWallet:Games:{gameId}:SigningKey"] = ordinaryKey is null ? null : Convert.ToBase64String(ordinaryKey),
        });
        builder.Services.AddSingleton(database);
        var app = builder.Build();
        app.MapGameWalletBatchSettlement();
        await app.StartAsync();
        return app;
    }

    private static ServiceProvider Worker(WebApplication app, byte[] key, string gameId = GameId)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(app.Environment);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GameWallet:Url"] = app.Urls.Single(),
            ["GameWallet:BatchSigningKey"] = Convert.ToBase64String(key),
        }).Build());
        services.AddGameWalletBatchClient(gameId);
        return services.BuildServiceProvider();
    }

    private static GameWalletBatchRequest Request(params GameWalletBatchEffect[] effects) =>
        new(Guid.NewGuid().ToString("N"), new string('a', 64), effects);

    private static Task Seed(FirestoreDb database, string account, long cents) =>
        FirestoreWalletDocuments.Balance(database, account, "slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["userId"] = account, ["currencyId"] = "slotsCredits", ["available"] = cents / 100,
            ["availableFractionalCents"] = cents % 100, ["reserved"] = 0L, ["version"] = 1L,
        });

    private static Task<long[]> Balances(FirestoreDb database) => Task.WhenAll(new[] { "alice", "bob", "carol" }
        .Select(account => FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, account, false, default)));
}
