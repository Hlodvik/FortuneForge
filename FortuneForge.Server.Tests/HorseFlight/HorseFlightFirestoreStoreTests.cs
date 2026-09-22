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

        Assert.Equal(started, retryStart);
        Assert.Equal(completed, retryCompletion);
        var run = Assert.Single((await database.Collection("horseFlightRuns").GetSnapshotAsync()).Documents);
        Assert.Equal(started.RunId, Field<string>(run, "runId"));
        Assert.Equal("player-1", Field<string>(run, "userId"));
        Assert.Equal((long)started.Seed, Field<long>(run, "seed"));
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

    private static CompleteHorseFlightRunRequest TerminalCompletion(uint seed)
    {
        var state = HorseFlightEngine.Start(seed);
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
