using FortuneForge.Games.HorseFlight;
using FortuneForge.Server.Arcade.HorseFlight;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Server.Tests.HorseFlight;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class HorseFlightFirestoreStoreTests(SolitaireFirestoreEmulatorFixture fixture)
{
    [Fact]
    public async Task FreeRunPersistsThePlayerBoundReplayAndBothLifecycleEventsExactlyOnce()
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-horse-flight-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var store = new HorseFlightFirestoreStore(database);

        var started = await store.StartAsync("player-1", "horse-flight-start-0001", default);
        var retryStart = await store.StartAsync("player-1", "horse-flight-start-0001", default);
        var completion = TerminalCompletion(started.Seed);
        var completed = await store.CompleteAsync(
            "player-1", started.RunId, completion, "horse-flight-complete-0001", default);
        var retryCompletion = await store.CompleteAsync(
            "player-1", started.RunId, completion, "horse-flight-complete-0001", default);

        Assert.Matches("^horse_flight_free_[0-9a-f]{64}$", started.RunId);
        Assert.False(started.WasReplay);
        Assert.True(retryStart.WasReplay);
        Assert.Equal(started.RunId, retryStart.RunId);
        Assert.Equal(started.Seed, retryStart.Seed);
        Assert.Equal(started.TickMilliseconds, retryStart.TickMilliseconds);
        Assert.False(completed.WasReplay);
        Assert.True(retryCompletion.WasReplay);
        Assert.Equal(completed.RunId, retryCompletion.RunId);
        Assert.Equal(completed.Score, retryCompletion.Score);
        Assert.Equal(completed.Phase, retryCompletion.Phase);
        Assert.Equal(completed.TotalTicks, retryCompletion.TotalTicks);
        Assert.Equal(completed.JumpTicks, retryCompletion.JumpTicks);
        Assert.Equal(completed.RightClickTicks, retryCompletion.RightClickTicks);
        var run = Assert.Single((await database.Collection("horseFlightRuns").GetSnapshotAsync()).Documents);
        Assert.Equal(started.RunId, Field<string>(run, "runId"));
        Assert.Equal("player-1", Field<string>(run, "userId"));
        Assert.Equal((long)started.Seed, Field<long>(run, "seed"));
        Assert.Equal((long)HorseFlightEngine.CurrentRulesVersion, Field<long>(run, "rulesVersion"));
        Assert.Equal(completed.Phase, Field<string>(run, "phase"));
        Assert.Equal((long)completion.TotalTicks, Field<long>(run, "totalTicks"));
        Assert.Equal((long)completed.Score, Field<long>(run, "score"));
        Assert.True(run.ContainsField("rightClickTicks"));
        Assert.Equal("horse-flight-start-0001", Field<string>(run, "startIdempotencyKey"));
        Assert.Equal("horse-flight-complete-0001", Field<string>(run, "completionIdempotencyKey"));
        Assert.True(run.ContainsField("createdAt"));
        Assert.True(run.ContainsField("completedAt"));

        var events = (await database.Collection("horseFlightRunEvents").GetSnapshotAsync()).Documents;
        Assert.Equal(2, events.Count);
        Assert.Contains(events, entry =>
            Field<string>(entry, "runId") == started.RunId &&
            Field<string>(entry, "userId") == "player-1" &&
            Field<string>(entry, "event") == "started" &&
            Field<string>(entry, "idempotencyKey") == "horse-flight-start-0001");
        Assert.Contains(events, entry =>
            Field<string>(entry, "runId") == started.RunId &&
            Field<string>(entry, "userId") == "player-1" &&
            Field<string>(entry, "event") == "completed" &&
            Field<string>(entry, "idempotencyKey") == "horse-flight-complete-0001");
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task FreeRunWithoutAStoredRulesVersionCompletesWithLegacyRules()
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-horse-flight-legacy-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var store = new HorseFlightFirestoreStore(database);
        var started = await store.StartAsync("legacy-player", "horse-flight-legacy-start", default);
        var runReference = database.Collection("horseFlightRuns").Document(started.RunId);
        await runReference.UpdateAsync(new Dictionary<string, object>
        {
            ["rulesVersion"] = FieldValue.Delete,
        });

        var completion = TerminalCompletion(started.Seed, HorseFlightEngine.LegacyRulesVersion);
        var completed = await store.CompleteAsync(
            "legacy-player", started.RunId, completion, "horse-flight-legacy-complete", default);

        Assert.NotEqual("running", completed.Phase);
        var run = await runReference.GetSnapshotAsync();
        Assert.Equal((long)HorseFlightEngine.LegacyRulesVersion, Field<long>(run, "rulesVersion"));
        Assert.Equal((long)completed.Score, Field<long>(run, "score"));

        await runReference.UpdateAsync(new Dictionary<string, object>
        {
            ["rulesVersion"] = FieldValue.Delete,
        });
        var retry = await store.CompleteAsync(
            "legacy-player", started.RunId, completion, "horse-flight-legacy-complete", default);
        Assert.True(retry.WasReplay);
        Assert.Equal(completed.Score, retry.Score);
    }

    [Fact]
    public async Task FreeRunStartedOnPreviousRulesCanFinishAfterRulesUpgrade()
    {
        _ = fixture;
        var database = new FirestoreDbBuilder
        {
            ProjectId = $"demo-horse-flight-v3-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
        var store = new HorseFlightFirestoreStore(database);
        var started = await store.StartAsync("previous-rules-player", "horse-flight-v3-start", default);
        var runReference = database.Collection("horseFlightRuns").Document(started.RunId);
        await runReference.UpdateAsync(new Dictionary<string, object>
        {
            ["rulesVersion"] = (long)HorseFlightEngine.PreviousRulesVersion,
        });

        var completion = TerminalCompletion(started.Seed, HorseFlightEngine.PreviousRulesVersion);
        var completed = await store.CompleteAsync(
            "previous-rules-player", started.RunId, completion, "horse-flight-v3-complete", default);

        Assert.NotEqual("running", completed.Phase);
        var run = await runReference.GetSnapshotAsync();
        Assert.Equal((long)HorseFlightEngine.PreviousRulesVersion, Field<long>(run, "rulesVersion"));
        Assert.Equal((long)completed.Score, Field<long>(run, "score"));
    }

    private static CompleteHorseFlightRunRequest TerminalCompletion(
        uint seed,
        int rulesVersion = HorseFlightEngine.CurrentRulesVersion)
    {
        var state = HorseFlightEngine.Start(seed, rulesVersion: rulesVersion);
        var totalTicks = 0;
        while (state.Phase == HorseFlightPhase.Running)
        {
            state = HorseFlightEngine.Step(state, HorseFlightInput.None).State;
            totalTicks++;
        }
        return new CompleteHorseFlightRunRequest(totalTicks, []);
    }

    private static T Field<T>(DocumentSnapshot document, string name) =>
        document.TryGetValue<T>(name, out var value)
            ? value
            : throw new InvalidOperationException($"Missing {name}.");
}
