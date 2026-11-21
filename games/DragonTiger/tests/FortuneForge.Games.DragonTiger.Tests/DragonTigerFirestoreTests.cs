using System.Text.Json;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using System.Net.Http.Json;
using FortuneForge.Games.Cards;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.DragonTiger.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;
using Xunit.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FortuneForge.Games.DragonTiger.Tests;

[CollectionDefinition("Dragon Tiger Firestore", DisableParallelization = true)]
public sealed class DragonTigerFirestoreCollection : ICollectionFixture<DragonTigerEmulator>;

[Collection("Dragon Tiger Firestore")]
public sealed class DragonTigerFirestoreTests(DragonTigerEmulator emulator)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PaidHttp_LostWalletResponseAndFeaturePauseKeepOriginalRecoveryRoutes()
    {
        var db = Database(); var wallet = new Wallet { LoseNextResponse = true };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration["Features:DragonTigerEnabled"] = "true";
        builder.Services.AddDragonTigerServer();
        builder.Services.AddSingleton(db);
        builder.Services.AddSingleton<IGameWalletClient>(wallet);
        builder.Services.AddSingleton<IGameWalletAccountReader>(wallet);
        await using var app = builder.Build();
        app.MapDragonTiger((_, _) => Task.FromResult<DragonTigerAccount?>(new("player", wallet.Balance / 100m)));
        await app.StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", "dragon_tiger_paid_http_01");
        var busy = await client.PostAsJsonAsync("/api/games/dragon-tiger/rounds", new CreateDragonTigerRoundRequest(.5m));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), busy.Headers.RetryAfter!.Delta);
        builder.Configuration["Features:DragonTigerEnabled"] = "false";
        var recovered = await client.GetFromJsonAsync<DragonTigerRoundResponse>("/api/games/dragon-tiger/rounds/latest");
        var replay = await client.PostAsJsonAsync("/api/games/dragon-tiger/rounds", new CreateDragonTigerRoundRequest(.5m));
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        var result = await replay.Content.ReadFromJsonAsync<DragonTigerRoundResponse>();
        Assert.Equal(recovered!.RoundId, result!.RoundId);
        Assert.Equal(recovered.Balance, result.Balance);
        Assert.Equal(recovered.DragonCard, result.DragonCard);
        Assert.False((await client.GetFromJsonAsync<DragonTigerStatusResponse>("/api/games/dragon-tiger/status"))!.Available);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "dragon_tiger_paid_http_02");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/games/dragon-tiger/rounds", new CreateDragonTigerRoundRequest(.5m))).StatusCode);
        Assert.Equal(1, wallet.Committed);
    }

    [Fact]
    public async Task SignedWallet_LostResponseRecoversAcrossRestartAndPauseExactlyOnce()
    {
        var db = Database(); var wallet = new Wallet { LoseNextResponse = true };
        const string key = "dragon_tiger_recovery_0001";
        var request = new CreateDragonTigerRoundRequest(10, 0, .5m);
        using (GameSecurityContext.Push(Observation("original-wager", "POST", "/api/games/dragon-tiger/rounds")))
            await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(db, wallet).StartAsync("player", request, key));
        Assert.Equal(10_950, wallet.Balance);
        Assert.Empty((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        using var recovery = GameSecurityContext.Push(Observation("recovery-request", "GET", "/api/games/dragon-tiger/rounds/latest"));
        var restarted = Service(db, wallet, enabled: false);
        var recovered = await restarted.LatestAsync("player");
        Assert.Equal(109.50m, recovered!.Balance);
        Assert.Equal(2, recovered.ShoeCardsUsed);
        var replay = await restarted.StartAsync("player", request, key);
        Assert.Equal(recovered.RoundId, replay.RoundId);
        Assert.Equal(recovered.Balance, replay.Balance);
        Assert.Equal(recovered.DragonCard, replay.DragonCard);
        Assert.Equal(1, wallet.Committed);
        Assert.Single((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection(RoundAuditDocuments.Summaries).GetSnapshotAsync()).Documents);
        Assert.Equal("original-wager", Assert.Single((await db.Collection(GameSecurityEventDocuments.CollectionName).GetSnapshotAsync()).Documents).GetValue<string>("requestId"));
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("userBalances").GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<DragonTigerDisabledException>(() => restarted.StartAsync("player", request, "dragon_tiger_disabled_0002"));
        await Assert.ThrowsAsync<DragonTigerRoundNotFoundException>(() => restarted.GetAsync("other", recovered.RoundId));
    }

    [Fact]
    public async Task SignedWallet_PendingDealBlocksAnotherWagerAndConflictingExactRetry()
    {
        var db = Database(); var wallet = new Wallet { LoseNextResponse = true };
        var service = Service(db, wallet);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.StartAsync("player", new(1), "dragon_tiger_pending_0001"));
        await Assert.ThrowsAsync<DragonTigerRoundConflictException>(() => service.StartAsync("player", new(2), "dragon_tiger_pending_0001"));
        await Assert.ThrowsAsync<DragonTigerRoundConflictException>(() => service.StartAsync("player", new(1), "dragon_tiger_pending_0002"));
        Assert.Equal(1, wallet.Committed);
        var recovered = await service.GetAsync("player", DragonTigerService.LookupKey("player", "dragon_tiger_pending_0001"));
        Assert.Equal(101m, recovered.Balance);
        var next = await service.StartAsync("player", new(1), "dragon_tiger_pending_0002");
        Assert.Equal(4, next.ShoeCardsUsed);
        Assert.Equal(2, wallet.Committed);
    }

    [Fact]
    public async Task SignedWallet_ConcurrentInstancesUseOneFrozenOutcomeAndSettlement()
    {
        var db = Database(); var wallet = new Wallet();
        var services = new[] { Service(db, wallet), Service(db, wallet, dragon: CardRank.Ace, tiger: CardRank.King) };
        var results = await Task.WhenAll(services.Select(service => service.StartAsync("player", new(.5m), "dragon_tiger_parallel_0001")));
        Assert.Single(results.Select(result => result.RoundId).Distinct());
        Assert.Single(results.Select(result => result.DragonCard).Distinct());
        Assert.Single(results.Select(result => result.Balance).Distinct());
        Assert.Equal(1, wallet.Committed);
        Assert.Equal(2, results[0].ShoeCardsUsed);
    }

    [Fact]
    public async Task SignedWallet_QuarterRandTieAndDeclineAreDurable()
    {
        var db = Database(); var wallet = new Wallet();
        var tied = Service(db, wallet, dragon: CardRank.Queen, tiger: CardRank.Queen);
        var result = await tied.StartAsync("player", new(.5m, 0, .5m), "dragon_tiger_tie_0001");
        Assert.Equal(4.75m, result.TotalReturn);
        Assert.Equal(103.75m, result.Balance);
        Assert.Equal("half-loss", result.Bets[0].Disposition);
        var poorDb = Database(); var poor = new Wallet { Balance = 0 };
        var service = Service(poorDb, poor);
        await Assert.ThrowsAsync<DragonTigerInsufficientCreditsException>(() => service.StartAsync("player", new(.5m), "dragon_tiger_poor_0001"));
        poor.Balance = 10000;
        await Assert.ThrowsAsync<DragonTigerInsufficientCreditsException>(() => service.StartAsync("player", new(.5m), "dragon_tiger_poor_0001"));
        Assert.Equal(0, poor.Committed);
        Assert.Empty((await poorDb.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
        var next = await service.StartAsync("player", new(.5m), "dragon_tiger_poor_0002");
        Assert.Equal(2, next.ShoeCardsUsed);
    }

    [Fact]
    public async Task CorruptShoe_IsServerFailureWithoutChargingAnotherWager()
    {
        var db = Database(); var wallet = new Wallet(); var service = Service(db, wallet);
        await service.StartAsync("player", new(.5m), "dragon_tiger_shoe_proof_1");
        var shoe = Assert.Single((await db.Collection("dragonTigerShoes").GetSnapshotAsync()).Documents);
        await shoe.Reference.UpdateAsync("cards", new[] { "invalid-card" });
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.StartAsync("player", new(.5m), "dragon_tiger_shoe_proof_2"));
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.LatestAsync("player"));
        Assert.Equal(1, wallet.Committed);
    }

    [Theory]
    [InlineData("intentSha256")]
    [InlineData("receiptSha256")]
    public async Task SignedWallet_CorruptEvidenceFailsClosedWithoutSettlement(string field)
    {
        var db = Database(); var wallet = new Wallet(); var service = Service(db, wallet);
        var result = await service.StartAsync("player", new(1), "dragon_tiger_integrity_0001");
        await db.Collection("dragonTigerRounds").Document(result.RoundId).UpdateAsync(field, "corrupt");
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.GetAsync("player", result.RoundId));
        Assert.Equal(1, wallet.Committed);
    }

    [Fact]
    public async Task PersistedIntentBytes_KeepOriginalWalletFingerprintAcrossRecovery()
    {
        var db = Database(); var wallet = new Wallet { FailBeforeNextCommit = true };
        const string key = "dragon_tiger_frozen_bytes_1";
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(db, wallet).StartAsync("player", new(.5m), key));
        Assert.Equal(0, wallet.Committed);
        var id = DragonTigerService.LookupKey("player", key);
        var record = db.Collection("dragonTigerRounds").Document(id);
        var pending = await record.GetSnapshotAsync();
        using var parsed = JsonDocument.Parse(pending.GetValue<string>("intentJson"));
        var originalBytes = JsonSerializer.Serialize(parsed.RootElement, new JsonSerializerOptions { WriteIndented = true });
        var digest = GameSecurityEventDocuments.Hash(originalBytes);
        await record.UpdateAsync(new Dictionary<string, object> { ["intentJson"] = originalBytes, ["intentSha256"] = digest });
        // Model an older serializer's successful wallet call whose response was lost.
        await wallet.SettleAsync("player", new(id, digest, 50, 100), default);
        var recovered = await Service(db, wallet).LatestAsync("player");
        Assert.Equal(id, recovered!.RoundId);
        Assert.Equal(100.5m, recovered.Balance);
        Assert.Equal(1, wallet.Committed);
        Assert.Single((await db.Collection(GameAnalyticsOutbox.CollectionName).GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData("wager")]
    [InlineData("payout")]
    [InlineData("event")]
    public async Task OrphanedLegacyProof_BlocksNewChargeForTheSameKey(string kind)
    {
        var db = Database(); var wallet = new Wallet();
        const string key = "dragon_tiger_orphaned_01";
        var id = DragonTigerService.LookupKey("player", key);
        var proof = kind == "event" ? db.Collection("dragonTigerRoundEvents").Document(id + "-settled")
            : db.Collection("balanceTransactions").Document($"dragon-tiger-{id}-{kind}");
        await proof.SetAsync(new Dictionary<string, object> { ["legacyProof"] = true });
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service(db, wallet).StartAsync("player", new(.5m), key));
        Assert.Equal(0, wallet.Committed);
        Assert.Empty((await db.Collection("dragonTigerRounds").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task LegacyRound_ExactRetryAndHistoryUseSavedCardsAndNeverCharge()
    {
        var db = Database(); var wallet = new Wallet { Balance = 12_345 };
        const string key = "dragon_tiger_legacy_0001";
        var id = DragonTigerService.LookupKey("player", key);
        var cards = DragonTigerTests.Shoe(CardRank.Ace, CardRank.King);
        await db.Collection("dragonTigerRounds").Document(id).SetAsync(new Dictionary<string, object>
        {
            ["schemaVersion"] = 1L, ["roundId"] = id, ["userId"] = "player", ["startIdempotencyKey"] = key,
            ["dragonStakeCents"] = 50L, ["tigerStakeCents"] = 0L, ["tieStakeCents"] = 0L, ["totalStakeCents"] = 50L,
            ["dragonCard"] = CardCode.Format(cards[0]), ["tigerCard"] = CardCode.Format(cards[1]), ["outcome"] = "tiger",
            ["totalReturnCents"] = 0L, ["profitCents"] = -50L, ["balanceAfterWagerCents"] = 9950L,
            ["balanceAfterSettlementCents"] = 9950L, ["shoeCardsUsed"] = 2L, ["shoeCardsRemaining"] = 414L,
        });
        await db.Collection("balanceTransactions").Document($"dragon-tiger-{id}-wager").SetAsync(new Dictionary<string, object>
        {
            ["transactionId"] = $"dragon-tiger-{id}-wager", ["userId"] = "player", ["currencyId"] = "slotsCredits",
            ["type"] = "dragon-tiger-wager", ["idempotencyKey"] = key, ["amount"] = -.5d, ["balanceAfter"] = 99.5d,
        });
        await db.Collection("dragonTigerRoundEvents").Document(id + "-settled").SetAsync(new Dictionary<string, object>
        { ["roundId"] = id, ["userId"] = "player", ["event"] = "settled", ["idempotencyKey"] = key, ["schemaVersion"] = 1L });
        await db.Collection("dragonTigerShoes").Document(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("player")))).SetAsync(new Dictionary<string, object>
        { ["userId"] = "player", ["schemaVersion"] = 1L, ["cards"] = cards.Select(CardCode.Format).ToArray(), ["nextIndex"] = 2L, ["latestRoundId"] = id });
        var service = Service(db, wallet, enabled: false);
        var replay = await service.StartAsync("player", new(.5m), key);
        Assert.Equal("tiger", replay.Outcome);
        Assert.Equal(123.45m, replay.Balance);
        Assert.Equal(id, (await service.LatestAsync("player"))!.RoundId);
        Assert.Equal(0, wallet.Committed);
        await db.Collection("balanceTransactions").Document($"dragon-tiger-{id}-wager").UpdateAsync("amount", 999d);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("player", new(.5m), key));
        Assert.Equal(0, wallet.Committed);
    }

    private static GameRequestObservation Observation(string id, string method, string path) => new(id, "player", "session", "127.0.0.1",
        Now.UtcDateTime, method, path, "dragon-tiger", null, null, null, null, "not-retained", "test");
    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-dragon-tiger-{Guid.NewGuid():N}", Endpoint = emulator.Endpoint,
        ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None,
    }.Build();
    private static DragonTigerService Service(FirestoreDb db, Wallet wallet, bool enabled = true,
        CardRank dragon = CardRank.King, CardRank tiger = CardRank.Ace) => new(new DragonTigerWalletStore(db, wallet, wallet, enabled),
            new FixedTimeProvider(), () => DragonTigerTests.Shoe(dragon, tiger));
    private sealed class FixedTimeProvider : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Wallet : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> receipts = new();
        public long Balance { get; set; } = 10000;
        public int Committed { get; private set; }
        public bool LoseNextResponse { get; set; }
        public bool FailBeforeNextCommit { get; set; }
        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken token) => Task.FromResult(new GameWalletAccountState(0, Balance));
        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken token)
        {
            lock (receipts)
            {
                if (FailBeforeNextCommit) { FailBeforeNextCommit = false; throw new GameWalletUnavailableException(); }
                var identity = accountId + "/" + request.OperationId;
                if (receipts.TryGetValue(identity, out var prior))
                {
                    if (prior.Request != request) throw new GameWalletConflictException();
                    return Task.FromResult(prior.Receipt with { WasAlreadySettled = true });
                }
                var decline = Balance < request.WagerCents;
                var before = Balance; var afterWager = decline ? before : before - request.WagerCents;
                Balance = decline ? before : afterWager + request.PayoutCents;
                var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId("dragon-tiger", accountId, request.OperationId),
                    decline ? null : GameWalletSettlementIdentity.CreateId("dragon-tiger", accountId, request.OperationId) + "-wager", !decline && request.PayoutCents > 0 ? GameWalletSettlementIdentity.CreateId("dragon-tiger", accountId, request.OperationId) + "-payout" : null,
                    before, afterWager, Balance, Now, false, decline);
                receipts.Add(identity, (request, receipt));
                if (!decline) Committed++;
                if (LoseNextResponse) { LoseNextResponse = false; throw new GameWalletUnavailableException(); }
                return Task.FromResult(receipt);
            }
        }
    }
}

public sealed class DragonTigerEmulator : IAsyncLifetime
{
    private readonly ConcurrentQueue<string> output = new();
    private Process? process;
    public string Endpoint { get; private set; } = string.Empty;
    public async Task InitializeAsync()
    {
        var existing = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
        if (!string.IsNullOrWhiteSpace(existing))
        {
            if (!existing.StartsWith("127.0.0.1:") && !existing.StartsWith("localhost:"))
                throw new InvalidOperationException("Dragon Tiger tests require a localhost Firestore emulator.");
            Endpoint = existing;
            return;
        }
        var jarDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "firebase", "emulators");
        var jar = Directory.GetFiles(jarDirectory, "cloud-firestore-emulator-*.jar").OrderDescending().First();
        var java = "java";
        if (OperatingSystem.IsWindows())
        {
            var adoptium = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium");
            if (Directory.Exists(adoptium)) java = Directory.GetDirectories(adoptium, "jdk-*").OrderDescending().Select(path => Path.Combine(path, "bin", "java.exe")).First(File.Exists);
        }
        using var reservation = new TcpListener(System.Net.IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((System.Net.IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var start = new ProcessStartInfo(java) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
        foreach (var arg in new[] { "-Xms32m", "-Xmx256m", "-jar", jar, "--host", "127.0.0.1", "--port", port.ToString(), "--project_id", "demo-dragon-tiger-tests" }) start.ArgumentList.Add(arg);
        process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, args) => { if (args.Data is { } line) output.Enqueue(line); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is { } line) output.Enqueue(line); };
        if (!process.Start()) throw new InvalidOperationException("Could not start the local Firestore emulator.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException(string.Join("\n", output));
            try
            {
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(250);
                await client.ConnectAsync("127.0.0.1", port, timeout.Token);
                Endpoint = $"127.0.0.1:{port}";
                return;
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException) { await Task.Delay(100); }
        }
        throw new TimeoutException(string.Join("\n", output));
    }
    public async Task DisposeAsync()
    {
        if (process is { HasExited: false })
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        process?.Dispose();
    }
}
