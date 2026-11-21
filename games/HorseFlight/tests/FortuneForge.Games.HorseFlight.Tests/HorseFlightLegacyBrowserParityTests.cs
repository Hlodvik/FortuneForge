using System.Text.Json;
using Xunit;

namespace FortuneForge.Games.HorseFlight.Tests;

public sealed class HorseFlightLegacyBrowserParityTests
{
    [Fact]
    public void ArchivedVersionOneBrowserGenerationAndAcceptedControlStreamsMatchTheAuthoritativeEngine()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "legacy-replay-vectors.json"));
        var vectors = JsonSerializer.Deserialize<ReplayVector[]>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(15, vectors.Length);
        foreach (var vector in vectors)
        {
            var initial = HorseFlightEngine.Start(vector.Seed, rulesVersion: vector.RulesVersion);
            Assert.Equal(vector.InitialRandom, initial.RandomState);
            Assert.Equal(vector.InitialPlatforms.Length, initial.Platforms.Length);
            foreach (var pair in vector.InitialPlatforms.Zip(initial.Platforms))
            {
                Assert.Equal(pair.First.Id, pair.Second.Id);
                Assert.Equal(pair.First.X, pair.Second.X, precision: 8);
                Assert.Equal(pair.First.Width, pair.Second.Width, precision: 8);
                Assert.Equal(pair.First.Y, pair.Second.Y, precision: 8);
            }
            var replay = new HorseFlightReplay(vector.TotalTicks, [.. vector.JumpTicks])
            { RulesVersion = vector.RulesVersion, RightClickTicks = [.. vector.RightClickTicks] };
            var outcome = HorseFlightReplayEvaluator.Evaluate(vector.Seed, replay).Snapshot;
            Assert.Equal(vector.TotalTicks, outcome.Tick);
            Assert.Equal(vector.Score, outcome.Score);
            Assert.Equal(vector.Phase, HorseFlightRunReplay.PhaseName(outcome.Phase));
        }
    }
    private sealed record PlatformVector(int Id, double X, double Width, double Y);
    private sealed record ReplayVector(uint Seed, string Policy, int RulesVersion, uint InitialRandom,
        PlatformVector[] InitialPlatforms, int TotalTicks, int[] JumpTicks, int[] RightClickTicks, int Score, string Phase);
}
