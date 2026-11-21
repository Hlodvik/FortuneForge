using System.Text.Json;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.ArcaneArchives;

/// <summary>Replays the frozen layout and final reel stops against the admitted Arcane rules.</summary>
public static class ArcaneNativeReplay
{
    public static void Validate(ClassicSlotSpinOutcome outcome, string accountId, long admittedWagerPoints,
        bool isFreeSpin, string engineVersion, ArcaneTomeState tome, ArcanePotionBottle? potion, int pityBefore,
        long energyBefore = 0)
    {
        if (outcome.SpinId == Guid.Empty || outcome.GameId != ArcaneArchivesEngine.GameId ||
            outcome.Audit.EngineVersion != engineVersion || outcome.WagerPoints != admittedWagerPoints ||
            outcome.IsFreeSpin != isFreeSpin || outcome.Audit.GeneratedAtUtc.Kind != DateTimeKind.Utc ||
            outcome.Audit.GenerationAttempts < 1 ||
            !outcome.FiveMatchPityTriggered && outcome.Audit.GenerationAttempts != 1 ||
            isFreeSpin && potion is not null)
            throw new InvalidOperationException("The frozen Arcane spin differs from its admission.");

        var tomeActive = tome.ActivePage is not null;
        var layout = ArcaneArchivesEngine.ReplayLayoutIndex(engineVersion, outcome.ReelSetId, tomeActive);
        var draws = new FrozenFinalDraws([layout, .. outcome.ReelStops]);
        var replay = new ArcaneArchivesEngine(draws, engineVersion).SpinTome(admittedWagerPoints, accountId,
            energyBefore, pityBefore, isFreeSpin, tomeActive, potion);

        // The original generation time and rejected pity redraws are outside the final-window evidence.
        var expected = replay with
        {
            SpinId = outcome.SpinId,
            Audit = replay.Audit with
            {
                GeneratedAtUtc = outcome.Audit.GeneratedAtUtc,
                GenerationAttempts = outcome.Audit.GenerationAttempts
            }
        };
        if (draws.Remaining != 0 || JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(outcome))
            throw new InvalidOperationException("The frozen Arcane payout or reel evidence does not replay.");
    }

    private sealed class FrozenFinalDraws(IEnumerable<int> indices) : IGameRandomSource
    {
        private readonly Queue<int> draws = new(indices);
        public int Remaining => draws.Count;

        public int Next(int exclusiveUpperBound)
        {
            if (!draws.TryDequeue(out var index) || index < 0 || index >= exclusiveUpperBound)
                throw new InvalidOperationException("The saved Arcane reel stops do not match the frozen layout.");
            return index;
        }
    }
}
