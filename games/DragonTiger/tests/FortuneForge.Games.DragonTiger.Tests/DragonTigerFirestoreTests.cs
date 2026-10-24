using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using FortuneForge.Games.Cards;
using FortuneForge.Games.DragonTiger.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;
using Xunit.Abstractions;

namespace FortuneForge.Games.DragonTiger.Tests;

[CollectionDefinition("Dragon Tiger Firestore", DisableParallelization = true)]
public sealed class DragonTigerFirestoreCollection : ICollectionFixture<DragonTigerEmulator>;

[Collection("Dragon Tiger Firestore")]
public sealed class DragonTigerFirestoreTests(DragonTigerEmulator emulator, ITestOutputHelper output)
{
    [Fact]
    public async Task AtomicLedger_ConcurrentInstancesReplayExactlyOnceAndRecoverAfterResponseLoss()
    {
        var database = Database();
        await Seed(database, "player", 100);
        var first = Service(database, CardRank.King, CardRank.Ace);
        var second = Service(database, CardRank.King, CardRank.Ace);
        const string key = "dragon_tiger_atomic_0001";
        var request = new CreateDragonTigerRoundRequest(10, 0, 0.50m);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var rounds = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => PlaceWithContentionRecovery(index)));
        Assert.Single(rounds.Select(round => round.RoundId).Distinct());
        Assert.All(rounds, round => Assert.Equal(109.50m, round.Balance));
        Assert.Single((await database.Collection("dragonTigerRounds").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("dragonTigerRoundEvents").GetSnapshotAsync()).Documents);
        var entries = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.GetValue<double>("amount") == -10.5);
        Assert.Contains(entries, entry => entry.GetValue<double>("amount") == 20.0);
        var restored = await second.GetAsync("player", rounds[0].RoundId);
        Assert.Equal(rounds[0].DragonCard, restored.DragonCard);
        Assert.Equal(rounds[0].RoundId, (await second.LatestAsync("player"))!.RoundId);
        Assert.Equal(2, restored.ShoeCardsUsed);
        var balance = await Balance(database, "player");
        Assert.Equal(109L, balance.GetValue<long>("available"));
        Assert.Equal(50L, balance.GetValue<long>("availableFractionalCents"));
        Assert.Equal(2L, balance.GetValue<long>("version"));
        await Assert.ThrowsAsync<DragonTigerRoundConflictException>(() => second.StartAsync("player", request with { TieStake = 1 }, key));
        await Assert.ThrowsAsync<DragonTigerRoundNotFoundException>(() => second.GetAsync("other", rounds[0].RoundId));
        Assert.Null(await second.LatestAsync("other"));
        Assert.Equal(2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);

        async Task<DragonTigerRoundResponse> PlaceWithContentionRecovery(int index)
        {
            // All eight initial requests still race. The emulator can hold
            // contested locks for up to 30 seconds, unlike production Firestore:
            // https://firebase.google.com/docs/emulator-suite/connect_firestore#transactions
            // Model the existing HTTP 503/Retry-After contract, never an entire
            // test retry: only a definitive Aborted retries this immutable key/body.
            const int maximumAttempts = 3;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await (index % 2 == 0 ? first : second).StartAsync("player", request, key, deadline.Token);
                }
                catch (RpcException exception) when (exception.StatusCode == StatusCode.Aborted &&
                    attempt < maximumAttempts && !deadline.IsCancellationRequested)
                {
                    output.WriteLine($"Emulator contention: caller {index}, attempt {attempt}; replaying the same request key.");
                    await Task.Delay(1_000 + Random.Shared.Next(0, 501), deadline.Token);
                }
            }
        }
    }

    [Fact]
    public async Task AtomicLedger_TiePreservesQuarterRandReturnAndIndependentTiePayout()
    {
        var database = Database();
        await Seed(database, "tie-player", 100);
        var service = Service(database, CardRank.Queen, CardRank.Queen);
        var request = new CreateDragonTigerRoundRequest(0.50m, 0, 0.50m);
        var result = await service.StartAsync("tie-player", request, "dragon_tiger_tied_0001");
        Assert.Equal("tie", result.Outcome);
        Assert.Equal("half-loss", result.Bets[0].Disposition);
        Assert.Equal(4.75m, result.TotalReturn);
        Assert.Equal(103.75m, result.Balance);
        var balance = await Balance(database, "tie-player");
        Assert.Equal(103L, balance.GetValue<long>("available"));
        Assert.Equal(75L, balance.GetValue<long>("availableFractionalCents"));
        var replay = await service.StartAsync("tie-player", request, "dragon_tiger_tied_0001");
        Assert.Equal(result.Balance, replay.Balance);
        Assert.Equal(result.RoundId, replay.RoundId);
        Assert.Equal(2, replay.ShoeCardsUsed);
    }

    [Fact]
    public async Task AtomicLedger_LosingBetWritesOnlyDebitAndInsufficientFundsWriteNothing()
    {
        var database = Database();
        await Seed(database, "poor", 1);
        var service = Service(database, CardRank.Ace, CardRank.King);
        await Assert.ThrowsAsync<DragonTigerInsufficientCreditsException>(() => service.StartAsync("poor", new(1.50m), "dragon_tiger_poor_0001"));
        Assert.Empty((await database.Collection("dragonTigerRounds").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        var result = await service.StartAsync("poor", new(0.50m), "dragon_tiger_loss_0001");
        Assert.Equal(0.50m, result.Balance);
        Assert.Equal(-0.50m, result.Profit);
        Assert.Equal(0m, result.TotalReturn);
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        var replay = await service.StartAsync("poor", new(0.50m), "dragon_tiger_loss_0001");
        Assert.Equal(result.Balance, replay.Balance);
    }

    [Fact]
    public async Task AtomicLedger_ConcurrentDifferentKeysAdvanceShoeWithoutOverdrawing()
    {
        var database = Database();
        await Seed(database, "concurrent", 100);
        var service = Service(database, CardRank.King, CardRank.Ace);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => service.StartAsync("concurrent", new(0.50m, 0.50m), $"dragon_tiger_concurrent_{index:0000}")));
        Assert.Equal(2, results.Select(round => round.RoundId).Distinct().Count());
        Assert.Equal(new[] { 2, 4 }, results.Select(round => round.ShoeCardsUsed).Order().ToArray());
        var totalProfit = results.Sum(round => round.Profit);
        var balance = await Balance(database, "concurrent");
        var cents = balance.GetValue<long>("available") * 100 + balance.GetValue<long>("availableFractionalCents");
        Assert.Equal(100m + totalProfit, cents / 100m);
        Assert.True(cents >= 0);
    }

    [Fact]
    public async Task AtomicLedger_CompetingWagersCannotOverdrawOneStakeBalance()
    {
        var database = Database();
        await Seed(database, "one-stake", 0);
        await database.Collection("userBalances").Document("one-stake_slotsCredits").UpdateAsync("availableFractionalCents", 50L);
        var service = Service(database, CardRank.Ace, CardRank.King);
        async Task<bool> Place(int index)
        {
            try { await service.StartAsync("one-stake", new(0.50m), $"dragon_tiger_compete_{index:0000}"); return true; }
            catch (DragonTigerInsufficientCreditsException) { return false; }
        }
        var outcomes = await Task.WhenAll(Place(1), Place(2));
        Assert.Single(outcomes, value => value);
        var balance = await Balance(database, "one-stake");
        Assert.Equal(0L, balance.GetValue<long>("available"));
        Assert.Equal(0L, balance.GetValue<long>("availableFractionalCents"));
        Assert.Single((await database.Collection("dragonTigerRounds").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task CorruptLedgerOrSettlement_FailsClosedWithoutAdditionalDebit()
    {
        var database = Database();
        await Seed(database, "player", 100);
        var service = Service(database, CardRank.King, CardRank.Ace);
        const string key = "dragon_tiger_corrupt_0001";
        var result = await service.StartAsync("player", new(1), key);
        await database.Collection("balanceTransactions").Document($"dragon-tiger-{result.RoundId}-payout").DeleteAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("player", new(1), key));
        Assert.Equal(101L, (await Balance(database, "player")).GetValue<long>("available"));
        await database.Collection("dragonTigerRounds").Document(result.RoundId).UpdateAsync("totalReturnCents", 999L);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync("player", result.RoundId));
        Assert.Equal(101L, (await Balance(database, "player")).GetValue<long>("available"));
    }

    [Theory]
    [InlineData("transactionId", "wrong")]
    [InlineData("idempotencyKey", "wrong")]
    [InlineData("balanceAfter", 999.0)]
    public async Task CorruptLedgerIdentityOrBalance_IsRejectedOnReplay(string field, object value)
    {
        var database = Database();
        await Seed(database, "player", 100);
        var service = Service(database, CardRank.King, CardRank.Ace);
        const string key = "dragon_tiger_ledger_0001";
        var result = await service.StartAsync("player", new(1), key);
        await database.Collection("balanceTransactions").Document($"dragon-tiger-{result.RoundId}-wager").UpdateAsync(field, value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("player", new(1), key));
        Assert.Equal(101L, (await Balance(database, "player")).GetValue<long>("available"));
    }

    [Theory]
    [InlineData("event", "wrong")]
    [InlineData("idempotencyKey", "wrong")]
    [InlineData("schemaVersion", 2L)]
    public async Task CorruptEventIdentity_IsRejectedOnReplay(string field, object value)
    {
        var database = Database();
        await Seed(database, "player", 100);
        var service = Service(database, CardRank.King, CardRank.Ace);
        const string key = "dragon_tiger_event_0001";
        var result = await service.StartAsync("player", new(1), key);
        await database.Collection("dragonTigerRoundEvents").Document($"{result.RoundId}-settled").UpdateAsync(field, value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("player", new(1), key));
        Assert.Equal(101L, (await Balance(database, "player")).GetValue<long>("available"));
    }

    [Fact]
    public async Task ExistingWalletWithoutAvailable_IsRejectedWithoutCreatingRound()
    {
        var database = Database();
        await database.Collection("userBalances").Document("corrupt_slotsCredits").SetAsync(new Dictionary<string, object> { ["availableFractionalCents"] = 50L });
        var service = Service(database, CardRank.King, CardRank.Ace);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("corrupt", new(0.50m), "dragon_tiger_badwallet_0001"));
        Assert.Empty((await database.Collection("dragonTigerRounds").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData("dragonCard", "invalid-card")]
    [InlineData("dragonStakeCents", -50L)]
    public async Task CorruptPersistedInput_ThrowsServerFailureInsteadOfUserValidation(string field, object value)
    {
        var database = Database();
        await Seed(database, "player", 100);
        var service = Service(database, CardRank.King, CardRank.Ace);
        var result = await service.StartAsync("player", new(1), "dragon_tiger_badinput_0001");
        await database.Collection("dragonTigerRounds").Document(result.RoundId).UpdateAsync(field, value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync("player", result.RoundId));
    }

    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-dragon-tiger-{Guid.NewGuid():N}", Endpoint = emulator.Endpoint,
        ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None,
    }.Build();
    private static DragonTigerService Service(FirestoreDb database, CardRank dragon, CardRank tiger) =>
        new(new DragonTigerFirestoreStore(database), TimeProvider.System, () => DragonTigerTests.Shoe(dragon, tiger));
    private static Task Seed(FirestoreDb database, string user, long whole) => database.Collection("userBalances").Document($"{user}_slotsCredits")
        .SetAsync(new Dictionary<string, object> { ["available"] = whole, ["availableFractionalCents"] = 0L, ["version"] = 1L });
    private static Task<DocumentSnapshot> Balance(FirestoreDb database, string user) => database.Collection("userBalances").Document($"{user}_slotsCredits").GetSnapshotAsync();
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
        foreach (var arg in new[] { "-jar", jar, "--host", "127.0.0.1", "--port", port.ToString(), "--project_id", "demo-dragon-tiger-tests" }) start.ArgumentList.Add(arg);
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
