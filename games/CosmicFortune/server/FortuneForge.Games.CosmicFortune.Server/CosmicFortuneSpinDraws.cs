using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.CosmicFortune.Server;

internal sealed record CosmicFortuneRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class CosmicFortuneSpinEvidence
{
    internal static CosmicFortuneRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin, string? engineVersion = null)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new CosmicFortuneEngine(recording, engineVersion ?? CosmicFortuneEngine.EngineVersion)
                .Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(CosmicFortuneRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new CosmicFortuneEngine(replay, audit.EngineVersion).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (CosmicFortuneRoundPolicy.OutcomeDigest(reproduced) != CosmicFortuneRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Cosmic Fortune’ recorded draws do not reproduce its prepared outcome.");
    }
}
