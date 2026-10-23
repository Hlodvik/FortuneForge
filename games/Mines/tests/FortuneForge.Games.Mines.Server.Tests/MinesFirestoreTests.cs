using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using FortuneForge.Games.Mines.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Games.Mines.Server.Tests;

[CollectionDefinition("Mines Firestore", DisableParallelization = true)]
public sealed class MinesFirestoreCollection : ICollectionFixture<MinesEmulator>;

[Collection("Mines Firestore")]
public sealed class MinesFirestoreTests(MinesEmulator emulator)
{
    private static string Key(int number) => $"mines-firestore-{number:00000000}";
    private FirestoreDb Database() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-mines-{Guid.NewGuid():N}", Endpoint = emulator.Endpoint,
        ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None,
    }.Build();
    private static MinesService Service(FirestoreDb database, int mines = 3) => new(new FirestoreMinesStore(database), new MinesEngine(new MinesStoreTests.FixedBoard(mines)));
    private static Task Seed(FirestoreDb database, string player, long cents) => database.Collection("userBalances").Document($"{player}_slotsCredits")
        .SetAsync(new Dictionary<string, object> { ["available"] = cents / 100, ["availableFractionalCents"] = cents % 100, ["version"] = 1L });
    private static async Task<long> Balance(FirestoreDb database, string player)
    {
        var document = await database.Collection("userBalances").Document($"{player}_slotsCredits").GetSnapshotAsync();
        return document.GetValue<long>("available") * 100 + document.GetValue<long>("availableFractionalCents");
    }

    [Fact]
    public async Task AtomicWagerRevealCashout_DuplicateInstancesAndRestartHaveOneDebitAndOneCredit()
    {
        var database = Database();
        await Seed(database, "player", 100_000);
        var first = Service(database); var second = Service(database);
        var starts = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => (index % 2 == 0 ? first : second).StartAsync("player", new(Key(1), 1_234, 3))));
        Assert.Single(starts.Select(start => start.Round!.RoundId).Distinct());
        Assert.All(starts, start => Assert.Equal(98_766, start.BalanceCents));
        Assert.False(starts[0].Practice);
        Assert.Single((await database.Collection("minesRounds").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        var id = starts[0].Round!.RoundId;
        var reveals = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => (index % 2 == 0 ? first : second).RevealAsync("player", id, new(Key(2), 24))));
        Assert.All(reveals, reveal => Assert.Equal(1, reveal.Round!.SafeReveals));
        var recovered = await Service(database).SessionAsync("player");
        Assert.Equal(JsonSerializer.Serialize(reveals[0]), JsonSerializer.Serialize(recovered));
        var cashouts = await Task.WhenAll(Enumerable.Range(0, 6).Select(index => (index % 2 == 0 ? first : second).CashOutAsync("player", id, new(Key(3)))));
        Assert.All(cashouts, cashout => Assert.Equal(100_154, cashout.BalanceCents));
        Assert.Equal(100_154, await Balance(database, "player"));
        var entries = (await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents;
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, entry => entry.GetValue<long>("amountCents") == -1_234);
        Assert.Contains(entries, entry => entry.GetValue<long>("amountCents") == 1_388);
        Assert.Equal(3, (await database.Collection("minesCommands").GetSnapshotAsync()).Count);
        var wallet = await database.Collection("userBalances").Document("player_slotsCredits").GetSnapshotAsync();
        Assert.Equal(3, wallet.GetValue<long>("version"));
        await Assert.ThrowsAsync<MinesConflictException>(() => Service(database).CashOutAsync("player", id, new(Key(4))));
    }

    [Fact]
    public async Task DifferentConcurrentStartsCannotOverdrawOrReplaceActiveBoard()
    {
        var database = Database();
        await Seed(database, "player", 100);
        async Task<bool> Place(int index)
        {
            try { await Service(database).StartAsync("player", new(Key(index), 100, 3)); return true; }
            catch (MinesConflictException) { return false; }
        }
        Assert.Single(await Task.WhenAll(Enumerable.Range(0, 6).Select(Place)), success => success);
        Assert.Equal(0, await Balance(database, "player"));
        Assert.Single((await database.Collection("minesRounds").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task CashoutRaceWithMineRevealSerializesSettlementExactlyOnce()
    {
        var database = Database();
        await Seed(database, "player", 10_000);
        var first = Service(database);
        var id = (await first.StartAsync("player", new(Key(1), 100, 3))).Round!.RoundId;
        await first.RevealAsync("player", id, new(Key(2), 24));
        async Task<bool> Attempt(Func<Task<MinesSessionResponse>> action)
        {
            try { await action(); return true; } catch (MinesConflictException) { return false; }
        }
        var outcomes = await Task.WhenAll(Attempt(() => first.CashOutAsync("player", id, new(Key(3)))), Attempt(() => Service(database).RevealAsync("player", id, new(Key(4), 0))));
        Assert.Single(outcomes, success => success);
        var final = await first.SessionAsync("player");
        Assert.Equal(final.Round!.Status == "lost" ? 9_900 : 10_012, await Balance(database, "player"));
        Assert.Equal(final.Round.Status == "lost" ? 1 : 2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task ReusedPayloadAndWrongPlayerCannotChangeWalletOrSeePrivateBoard()
    {
        var database = Database();
        await Seed(database, "player", 100_000);
        var service = Service(database);
        var start = await service.StartAsync("player", new(Key(1), 100, 3));
        var id = start.Round!.RoundId;
        await Assert.ThrowsAsync<MinesConflictException>(() => service.StartAsync("player", new(Key(1), 101, 3)));
        await Assert.ThrowsAsync<MinesConflictException>(() => service.RevealAsync("player", id, new(Key(1), 24)));
        await Assert.ThrowsAsync<MinesNotFoundException>(() => service.RevealAsync("attacker", id, new(Key(2), 24)));
        await Assert.ThrowsAsync<MinesNotFoundException>(() => service.CashOutAsync("attacker", id, new(Key(3))));
        Assert.Null((await service.SessionAsync("attacker")).Round);
        var publicJson = JsonSerializer.Serialize(start);
        Assert.DoesNotContain("MinePositions", publicJson);
        Assert.All(start.Round.Tiles, tile => Assert.Equal("hidden", tile.Kind));
        Assert.Equal(99_900, await Balance(database, "player"));
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task InsufficientMissingOrCorruptWallet_FailsClosedBeforeGeneratingBoard()
    {
        var database = Database();
        await Seed(database, "poor", 9);
        var generator = new MinesStoreTests.FixedBoard(3);
        var service = new MinesService(new FirestoreMinesStore(database), new MinesEngine(generator));
        await Assert.ThrowsAsync<MinesInsufficientFundsException>(() => service.StartAsync("poor", new(Key(1), 10, 3)));
        await Assert.ThrowsAsync<MinesInsufficientFundsException>(() => service.StartAsync("missing", new(Key(1), 10, 3)));
        await database.Collection("userBalances").Document("broken_slotsCredits").SetAsync(new Dictionary<string, object> { ["availableFractionalCents"] = 50L });
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync("broken", new(Key(1), 10, 3)));
        Assert.Equal(0, generator.Calls);
        Assert.Empty((await database.Collection("minesRounds").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task HistoricalCommandReplayDoesNotReplaceNewActiveRoundOrCreditAgain()
    {
        var database = Database();
        await Seed(database, "player", 100_000);
        var service = Service(database);
        var request = new StartMinesRequest(Key(1), 100, 3);
        var original = await service.StartAsync("player", request);
        var id = original.Round!.RoundId;
        await service.RevealAsync("player", id, new(Key(2), 24));
        await service.CashOutAsync("player", id, new(Key(3)));
        var paidReplay = await Service(database).CashOutAsync("player", id, new(Key(3)));
        Assert.Equal(100_012, paidReplay.BalanceCents);
        var next = await service.StartAsync("player", new(Key(4), 50, 3));
        var oldReceipt = await Service(database).StartAsync("player", request);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(oldReceipt));
        var current = await Service(database).SessionAsync("player");
        Assert.Equal(JsonSerializer.Serialize(next), JsonSerializer.Serialize(current));
        Assert.Equal(next.BalanceCents, await Balance(database, "player"));
        Assert.Equal(3, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task MaximumAutoWinSurvivesFirestoreRoundTripWithoutOverflow()
    {
        var database = Database();
        await Seed(database, "player", 100_000);
        var service = Service(database, 12);
        var id = (await service.StartAsync("player", new(Key(1), 10_000, 12))).Round!.RoundId;
        for (var tile = 12; tile < 25; tile++) await service.RevealAsync("player", id, new(Key(tile), tile));
        var restored = await Service(database).SessionAsync("player");
        Assert.Equal(51_482_970_000, restored.Round!.PayoutCents);
        Assert.Equal(51_483_060_000, await Balance(database, "player"));
        Assert.Equal(2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        await Service(database).RevealAsync("player", id, new(Key(24), 24));
        Assert.Equal(51_483_060_000, await Balance(database, "player"));
    }
}

public sealed class MinesEmulator : IAsyncLifetime
{
    private readonly ConcurrentQueue<string> output = new();
    private Process? process;
    public string Endpoint { get; private set; } = string.Empty;
    public async Task InitializeAsync()
    {
        var existing = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
        if (!string.IsNullOrWhiteSpace(existing))
        {
            if (!existing.StartsWith("127.0.0.1:") && !existing.StartsWith("localhost:")) throw new InvalidOperationException("Mines tests require a localhost Firestore emulator.");
            Endpoint = existing; return;
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
        foreach (var argument in new[] { "-jar", jar, "--host", "127.0.0.1", "--port", port.ToString(), "--project_id", "demo-mines-tests" }) start.ArgumentList.Add(argument);
        process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, args) => { if (args.Data is { } line) output.Enqueue(line); };
        process.ErrorDataReceived += (_, args) => { if (args.Data is { } line) output.Enqueue(line); };
        if (!process.Start()) throw new InvalidOperationException("Could not start the local Mines Firestore emulator.");
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException(string.Join("\n", output));
            try
            {
                using var client = new TcpClient(); using var timeout = new CancellationTokenSource(250);
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
