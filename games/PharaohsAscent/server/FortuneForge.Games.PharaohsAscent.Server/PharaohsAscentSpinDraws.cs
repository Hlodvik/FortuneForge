using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.PharaohsAscent.Server;

internal sealed record PharaohsAscentRoundEngineResult(ClassicSlotSpinOutcome Outcome, byte[] Draws);

public static partial class PharaohsAscentSpinEvidence
{
    internal static PharaohsAscentRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin, string? engineVersion = null)
    {
        var (outcome, draws) = ClassicSlotReplayEvidence.Record(random,
            recording => new PharaohsAscentEngine(recording, engineVersion ?? PharaohsAscentEngine.EngineVersion)
                .Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(outcome, draws);
    }

    internal static void Validate(PharaohsAscentRoundEngineResult engine, string accountId)
    {
        var outcome = engine.Outcome;
        var audit = outcome.Audit;
        var reproduced = ClassicSlotReplayEvidence.Reproduce(outcome, engine.Draws, replay =>
            new PharaohsAscentEngine(replay, audit.EngineVersion).Spin(outcome.WagerPoints, accountId, outcome.SpecialBoostApplied,
                audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, outcome.IsFreeSpin));
        if (PharaohsAscentRoundPolicy.OutcomeDigest(reproduced) != PharaohsAscentRoundPolicy.OutcomeDigest(outcome))
            throw new InvalidOperationException("Pharaoh’s Ascent’ recorded draws do not reproduce its prepared outcome.");
    }
}
