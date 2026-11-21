using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.GodsOfOlympus.Server;

internal sealed record OlympusRoundEngineResult(GodsOfOlympusTumbleSpinOutcome? Tumble, ClassicSlotSpinOutcome? Classic,
    byte[]? ClassicDraws, DateTime GeneratedAtUtc)
{
    internal Guid SpinId => Tumble?.SpinId ?? Classic?.SpinId ?? Guid.Empty;
}

public static partial class OlympusSpinEvidence
{
    internal static OlympusRoundEngineResult Record(IGameRandomSource random, string accountId, long wagerPoints,
        bool useBoost, long energy, string? mode, int pity, bool isFreeSpin)
    {
        if (!isFreeSpin)
        {
            var tumble = new GodsOfOlympusEngine(random).SpinTumble(wagerPoints);
            return new(tumble, null, null, DateTime.UtcNow);
        }

        var (classic, draws) = ClassicSlotReplayEvidence.Record(random, recording =>
            new GodsOfOlympusEngine(recording).Spin(wagerPoints, accountId, useBoost, energy, mode, pity, isFreeSpin));
        return new(null, classic, draws, classic.Audit.GeneratedAtUtc);
    }

    internal static void Validate(OlympusRoundEngineResult engine, string accountId)
    {
        if (engine.GeneratedAtUtc.Kind != DateTimeKind.Utc || engine.SpinId == Guid.Empty)
            throw InvalidDraws();

        OlympusRoundEngineResult replayed;
        if (engine is { Tumble: { } tumble, Classic: null, ClassicDraws: null })
        {
            if (!OlympusTumbleRules.SupportsPaidEngineVersion(tumble.EngineVersion) || tumble.DrawIndexes is null)
                throw InvalidDraws();
            var replay = new TumbleDrawReplay(tumble.DrawIndexes);
            var reproduced = new GodsOfOlympusEngine(replay).SpinTumble(tumble.WagerPoints, engineVersion: tumble.EngineVersion);
            if (!replay.Finished) throw InvalidDraws();
            replayed = new(reproduced with { SpinId = tumble.SpinId }, null, null, engine.GeneratedAtUtc);
        }
        else if (engine is { Tumble: null, Classic: { } classic, ClassicDraws: { } classicDraws } &&
            engine.GeneratedAtUtc == classic.Audit.GeneratedAtUtc && classic.IsFreeSpin)
        {
            var audit = classic.Audit;
            var reproduced = ClassicSlotReplayEvidence.Reproduce(classic, classicDraws, replay =>
                new GodsOfOlympusEngine(replay).Spin(classic.WagerPoints, accountId, classic.SpecialBoostApplied,
                    audit.EnergyBalanceBefore, audit.FeatureMode, audit.PityMissesBefore, isFreeSpin: true));
            replayed = new(null, reproduced, classicDraws, engine.GeneratedAtUtc);
        }
        else throw InvalidDraws();

        if (OlympusRoundPolicy.OutcomeDigest(replayed) != OlympusRoundPolicy.OutcomeDigest(engine)) throw InvalidDraws();
    }

    private static InvalidOperationException InvalidDraws() => new("The recorded Olympus draws do not reproduce the prepared outcome.");

    // The multiplier-weighted tumble draw domain is larger than a two-byte classic index.
    private sealed class TumbleDrawReplay(IReadOnlyList<int> draws) : IGameRandomSource
    {
        private int offset;
        internal bool Finished => offset == draws.Count;

        public int Next(int maximumExclusive)
        {
            if (offset >= draws.Count) throw InvalidDraws();
            var value = draws[offset++];
            if ((uint)value >= (uint)maximumExclusive) throw InvalidDraws();
            return value;
        }
    }
}
