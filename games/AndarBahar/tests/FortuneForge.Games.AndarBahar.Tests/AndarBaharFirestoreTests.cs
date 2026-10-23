using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using FortuneForge.Games.AndarBahar.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Games.AndarBahar.Tests;

[CollectionDefinition("Andar Bahar Firestore", DisableParallelization = true)]
public sealed class AndarBaharFirestoreCollection : ICollectionFixture<AndarBaharEmulator>;

[Collection("Andar Bahar Firestore")]
public sealed class AndarBaharFirestoreTests(AndarBaharEmulator emulator)
{
    [Fact]
    public async Task PaidTransaction_ConcurrentServerInstancesReplayOnceAndRecoverAfterLostResponse()
    {
        var db = Database();
        await Seed(db, "player", 10000);
        var first = Service(db, true);
        var second = Service(db, false);
        const string key = "andar_paid_concurrent_01";
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => first.StartAsync("player", new("andar", .50m), key)));
        Assert.Single(results.Select(result => result.RoundId).Distinct());
        Assert.All(results, result => { Assert.Equal(.45m, result.Profit); Assert.Equal(.95m, result.TotalReturn); Assert.Equal(100.45m, result.Balance); });
        var result = results[0];
        var recovered = await second.StartAsync("player", new("andar", .50m), key);
        Assert.Equal(result.RoundId, recovered.RoundId);
        Assert.Equal("andar", recovered.Winner);
        Assert.Single(recovered.Deals);
        Assert.Equal(result.RoundId, (await second.LatestAsync("player"))!.RoundId);
        Assert.Equal(result.RoundId, (await second.GetAsync("player", result.RoundId)).RoundId);
        Assert.Single((await db.Collection("andarBaharRounds").GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection("andarBaharRoundEvents").GetSnapshotAsync()).Documents);
        var ledger = (await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(2, ledger.Count);
        Assert.Contains(ledger, item => item.GetValue<long>("amountCents") == -50 && item.GetValue<double>("amount") == -.5);
        Assert.Contains(ledger, item => item.GetValue<long>("amountCents") == 95 && item.GetValue<double>("amount") == .95);
        var balance = await Wallet(db, "player").GetSnapshotAsync();
        Assert.Equal(100L, balance.GetValue<long>("available"));
        Assert.Equal(45L, balance.GetValue<long>("availableFractionalCents"));
        Assert.Equal(2L, balance.GetValue<long>("version"));
        await Assert.ThrowsAsync<AndarBaharRoundConflictException>(() => second.StartAsync("player", new("bahar", .50m), key));
        await Assert.ThrowsAsync<AndarBaharRoundNotFoundException>(() => second.GetAsync("other", result.RoundId));
        Assert.Null(await second.LatestAsync("other"));
        Assert.Equal(2, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        var record = JsonNode.Parse((await db.Collection("andarBaharRounds").Document(result.RoundId).GetSnapshotAsync()).GetValue<string>("recordJson"))!;
        Assert.Equal(2, record["RevealedCards"]!.AsArray().Count);
        Assert.False(record.AsObject().ContainsKey("Deck"));
    }
    [Fact]
    public async Task PaidTransaction_BaharWinPaysEvenMoneyAndOldReplayUsesCurrentBalance()
    {
        var db = Database();
        await Seed(db, "player", 10000);
        var service = Service(db, false);
        var first = await service.StartAsync("player", new("bahar", .50m), "bahar_paid_win_000001");
        Assert.Equal("bahar", first.Winner);
        Assert.Equal(.50m, first.Profit);
        Assert.Equal(1m, first.TotalReturn);
        Assert.Equal(100.50m, first.Balance);
        var second = await service.StartAsync("player", new("andar", .50m), "bahar_paid_next_00001");
        Assert.Equal(100m, second.Balance);
        var replay = await service.StartAsync("player", new("bahar", .50m), "bahar_paid_win_000001");
        Assert.Equal(first.RoundId, replay.RoundId);
        Assert.Equal(100m, replay.Balance);
        Assert.Equal(second.RoundId, (await service.LatestAsync("player"))!.RoundId);
    }
    [Fact]
    public async Task PaidTransaction_InsufficientBalanceAndConcurrentLossesCannotOverdraw()
    {
        var db = Database();
        await Seed(db, "player", 50);
        var service = Service(db, false);
        await Assert.ThrowsAsync<AndarBaharInsufficientCreditsException>(() => service.StartAsync("player", new("andar", 1), "andar_poor_balance_001"));
        Assert.Empty((await db.Collection("andarBaharRounds").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        async Task<bool> Place(int i)
        {
            try { await Service(db, false).StartAsync("player", new("andar", .50m), $"andar_competing_loss_{i:000}"); return true; }
            catch (AndarBaharInsufficientCreditsException) { return false; }
        }
        var placed = await Task.WhenAll(Place(1), Place(2));
        Assert.Single(placed, result => result);
        Assert.Equal(0, await new AndarBaharFirestoreStore(db).BalanceAsync("player", default));
        Assert.Single((await db.Collection("andarBaharRounds").GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }
    [Fact]
    public async Task PaidTransaction_DistinctConcurrentKeysCommitEachExactlyOnce()
    {
        var db = Database();
        await Seed(db, "player", 10000);
        var results = await Task.WhenAll(Enumerable.Range(1, 4).Select(i => Service(db, true).StartAsync("player", new("andar", .50m), $"andar_distinct_paid_{i:000}")));
        Assert.Equal(4, results.Select(round => round.RoundId).Distinct().Count());
        Assert.Equal(10180, await new AndarBaharFirestoreStore(db).BalanceAsync("player", default));
        Assert.Equal(4, (await db.Collection("andarBaharRounds").GetSnapshotAsync()).Count);
        Assert.Equal(8, (await db.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }
    [Theory]
    [InlineData("transactionId", "wrong")]
    [InlineData("idempotencyKey", "wrong")]
    [InlineData("balanceAfterCents", 999L)]
    [InlineData("amountCents", 999L)]
    public async Task PaidTransaction_TamperedLedgerFailsClosedWithoutAdditionalDebit(string field, object value)
    {
        var db = Database();
        await Seed(db, "player", 10000);
        var service = Service(db, true);
        const string key = "andar_tampered_ledger_01";
        var round = await service.StartAsync("player", new("andar", .50m), key);
        await db.Collection("balanceTransactions").Document($"andar-bahar-{round.RoundId}-wager").UpdateAsync(field, value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("player", new("andar", .50m), key));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync("player", round.RoundId));
        Assert.Equal(10045, await new AndarBaharFirestoreStore(db).BalanceAsync("player", default));
    }
    [Fact]
    public async Task PaidTransaction_MissingPayoutOrCorruptSettlementNeverRecreatesLedger()
    {
        var db = Database();
        await Seed(db, "player", 10000);
        var service = Service(db, true);
        const string key = "andar_missing_payout_01";
        var round = await service.StartAsync("player", new("andar", .50m), key);
        await db.Collection("balanceTransactions").Document($"andar-bahar-{round.RoundId}-payout").DeleteAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("player", new("andar", .50m), key));
        var reference = db.Collection("andarBaharRounds").Document(round.RoundId);
        var record = JsonNode.Parse((await reference.GetSnapshotAsync()).GetValue<string>("recordJson"))!;
        record["ProfitCents"] = 500;
        await reference.UpdateAsync("recordJson", record.ToJsonString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync("player", round.RoundId));
        Assert.Equal(10045, await new AndarBaharFirestoreStore(db).BalanceAsync("player", default));
        Assert.Single((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }
    [Theory]
    [InlineData("event", "wrong")]
    [InlineData("idempotencyKey", "wrong")]
    [InlineData("schemaVersion", 2L)]
    public async Task PaidTransaction_TamperedEventIsRejected(string field, object value)
    {
        var db = Database();
        await Seed(db, "player", 10000);
        var service = Service(db, true);
        const string key = "andar_tampered_event_01";
        var round = await service.StartAsync("player", new("andar", .50m), key);
        await db.Collection("andarBaharRoundEvents").Document($"{round.RoundId}-settled").UpdateAsync(field, value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("player", new("andar", .50m), key));
        Assert.Equal(10045, await new AndarBaharFirestoreStore(db).BalanceAsync("player", default));
    }
    [Fact]
    public async Task PaidTransaction_MalformedWalletIsNotTreatedAsFreeStartingBalance()
    {
        var db = Database();
        await Wallet(db, "player").SetAsync(new Dictionary<string, object> { ["availableFractionalCents"] = 50L });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, true).StartAsync("player", new("andar", .50m), "andar_malformed_wallet_1"));
        Assert.Empty((await db.Collection("andarBaharRounds").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }
    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-andar-bahar-{Guid.NewGuid():N}", Endpoint = emulator.Endpoint,
        ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None,
    }.Build();
    private static AndarBaharService Service(FirestoreDb db, bool andarWins) => AndarBaharServerTests.Service(new AndarBaharFirestoreStore(db), andarWins);
    private static DocumentReference Wallet(FirestoreDb db, string user) => db.Collection("userBalances").Document($"{user}_slotsCredits");
    private static Task Seed(FirestoreDb db, string user, long cents) => Wallet(db, user).SetAsync(new Dictionary<string, object>
    { ["available"] = cents / 100, ["availableFractionalCents"] = cents % 100, ["version"] = 1L });
}

public sealed class AndarBaharEmulator : IAsyncLifetime
{
    private readonly ConcurrentQueue<string> output = new();
    private Process? process;
    public string Endpoint { get; private set; } = string.Empty;
    public async Task InitializeAsync()
    {
        var existing = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
        if (!string.IsNullOrWhiteSpace(existing))
        {
            if (!existing.StartsWith("127.0.0.1:", StringComparison.Ordinal) && !existing.StartsWith("localhost:", StringComparison.Ordinal))
                throw new InvalidOperationException("Andar Bahar tests require a localhost Firestore emulator.");
            Endpoint = existing;
            return;
        }
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "firebase", "emulators");
        var jar = Directory.GetFiles(directory, "cloud-firestore-emulator-*.jar").OrderDescending().First();
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
        foreach (var arg in new[] { "-jar", jar, "--host", "127.0.0.1", "--port", port.ToString(), "--project_id", "demo-andar-bahar-tests" }) start.ArgumentList.Add(arg);
        process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, args) => { if (args.Data is { } line) output.Enqueue(line); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is { } line) output.Enqueue(line); };
        if (!process.Start()) throw new InvalidOperationException("Could not start the local Firestore emulator.");
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        var deadline = Environment.TickCount64 + 45_000;
        while (Environment.TickCount64 < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException(string.Join("\n", output));
            try
            {
                using var client = new TcpClient();
                using var timeout = new CancellationTokenSource(250);
                await client.ConnectAsync("127.0.0.1", port, timeout.Token);
                Endpoint = $"127.0.0.1:{port}"; return;
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException) { await Task.Delay(100); }
        }
        throw new TimeoutException(string.Join("\n", output));
    }
    public async Task DisposeAsync()
    {
        if (process is { HasExited: false }) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        process?.Dispose();
    }
}
