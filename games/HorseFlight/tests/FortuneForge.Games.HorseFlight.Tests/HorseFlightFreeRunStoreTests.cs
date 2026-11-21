using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.HorseFlight.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.HorseFlight.Tests;

public sealed class HorseFlightFreeRunStoreTests
{
    [FirestoreEmulatorFact]
    public async Task ReplicasRecoverLostResponsesAndKeepTheOriginalCompletionBalance()
    {
        var db = Connect();
        var wallet = new AccountReader { Balance = 12345 };
        var worker = Store(db, wallet, 17);
        var replica = Store(db, wallet, 99);
        var starts = await Task.WhenAll(worker.StartAsync("alice", "horse-flight-start-0001", default),
            replica.StartAsync("alice", "horse-flight-start-0001", default));
        Assert.Equal(starts[0].RunId, starts[1].RunId);
        Assert.Equal(starts[0].Seed, starts[1].Seed);
        Assert.Single(starts, start => !start.WasReplay);
        var replay = Terminal(starts[0].Seed, HorseFlightEngine.CurrentRulesVersion);
        var first = await worker.CompleteAsync("alice", starts[0].RunId, replay, "horse-flight-finish-0001", default);
        wallet.Balance = 45678;
        wallet.Unavailable = true;
        var retry = await replica.CompleteAsync("alice", starts[0].RunId, replay, "horse-flight-finish-0001", default);
        Assert.True(retry.WasReplay);
        Assert.Equal(123.45m, first.Balance);
        Assert.Equal(first.Balance, retry.Balance);
        Assert.Equal(first.Score, retry.Score);
        Assert.Equal(first.TotalTicks, retry.TotalTicks);
        Assert.Equal(2, (await db.Collection("horseFlightRunEvents").GetSnapshotAsync()).Count);
        Assert.Equal(2, (await db.Collection("gameSecurityEvents").GetSnapshotAsync()).Count);
        Assert.Empty((await db.Collection("userBalances").GetSnapshotAsync()).Documents);
        Assert.Empty((await db.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        await Assert.ThrowsAsync<HorseFlightRunNotFoundException>(() => replica.CompleteAsync("bob", first.RunId, replay, "horse-flight-finish-0002", default));
        await Assert.ThrowsAsync<HorseFlightRunConflictException>(() => replica.CompleteAsync("alice", first.RunId, replay with { TotalTicks = replay.TotalTicks + 1 }, "horse-flight-finish-0001", default));
        var doc = db.Collection("horseFlightRuns").Document(first.RunId);
        await doc.UpdateAsync("score", first.Score + 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => replica.CompleteAsync("alice", first.RunId, replay, "horse-flight-finish-0001", default));
    }

    [FirestoreEmulatorFact]
    public async Task EveryHistoricalRulesVersionResumesUsingThePersistedRules()
    {
        var db = Connect();
        var worker = Store(db, new AccountReader(), 41);
        foreach (var version in new[] { HorseFlightEngine.LegacyRulesVersion, HorseFlightEngine.DraftMultiBiomeRulesVersion,
            HorseFlightEngine.PreviousRulesVersion, HorseFlightEngine.CurrentRulesVersion })
        {
            var opened = await worker.StartAsync("alice", $"horse-flight-legacy-start-{version}", default);
            var doc = db.Collection("horseFlightRuns").Document(opened.RunId);
            await doc.UpdateAsync(new Dictionary<string, object>
            {
                ["rulesVersion"] = version == HorseFlightEngine.LegacyRulesVersion ? FieldValue.Delete : (object)(long)version,
            });
            var request = Terminal(opened.Seed, version);
            var completed = await worker.CompleteAsync("alice", opened.RunId, request, $"horse-flight-legacy-finish-{version}", default);
            Assert.NotEqual("running", completed.Phase);
            Assert.Equal(version, (await doc.GetSnapshotAsync()).GetValue<long>("rulesVersion"));
            var recovered = await Store(db, new AccountReader(), 99).CompleteAsync("alice", opened.RunId, request, $"horse-flight-legacy-finish-{version}", default);
            Assert.True(recovered.WasReplay);
            Assert.Equal(completed.Score, recovered.Score);
        }
    }

    [FirestoreEmulatorFact]
    public async Task InvalidTimelineDoesNotCompleteTheCommittedRun()
    {
        var db = Connect();
        var worker = Store(db, new AccountReader(), 41);
        var start = await worker.StartAsync("alice", "horse-flight-invalid-start", default);
        await Assert.ThrowsAsync<ArgumentException>(() => worker.CompleteAsync("alice", start.RunId,
            new CompleteHorseFlightRunRequest(1, []), "horse-flight-invalid-finish", default));
        Assert.Equal("running", (await db.Collection("horseFlightRuns").Document(start.RunId).GetSnapshotAsync()).GetValue<string>("phase"));
        Assert.Single((await db.Collection("horseFlightRunEvents").GetSnapshotAsync()).Documents);
        Assert.Single((await db.Collection("gameSecurityEvents").GetSnapshotAsync()).Documents);
    }

    private static CompleteHorseFlightRunRequest Terminal(uint seed, int version)
    {
        var state = HorseFlightEngine.Start(seed, rulesVersion: version);
        var ticks = 0;
        while (state.Phase == HorseFlightPhase.Running && ticks < HorseFlightReplayEvaluator.MaximumTicks)
        {
            state = HorseFlightEngine.Step(state, HorseFlightInput.None).State;
            ticks++;
        }
        Assert.NotEqual(HorseFlightPhase.Running, state.Phase);
        return new CompleteHorseFlightRunRequest(ticks, []);
    }
    private static HorseFlightFreeRunStore Store(FirestoreDb db, IGameWalletAccountReader wallet, uint seed) => new(db, wallet, createSeed: () => seed);
    private static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"demo-horse-{Guid.NewGuid():N}"[..29], EmulatorDetection = EmulatorDetection.EmulatorOnly,
    }.Build();
    private sealed class AccountReader : IGameWalletAccountReader
    {
        public long Balance { get; set; }
        public bool Unavailable { get; set; }
        public Task<GameWalletAccountState> GetStateAsync(string id, CancellationToken token) => Unavailable ? throw new GameWalletUnavailableException() : Task.FromResult(new GameWalletAccountState(0, Balance));
    }
}

public sealed class FirestoreEmulatorFactAttribute : FactAttribute
{
    public FirestoreEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "Run with a local Firestore emulator to verify Horse Flight persistence.";
    }
}
