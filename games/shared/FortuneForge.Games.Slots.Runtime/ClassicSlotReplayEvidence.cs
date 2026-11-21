using FortuneForge.Games.Slots;

namespace FortuneForge.Games.Slots.Runtime;

/// <summary>The existing audit envelope for a classic slot outcome.</summary>
public sealed record ClassicSlotRecordedAudit(string EngineVersion, DateTime GeneratedAtUtc, GameDefinition BaseGame,
    GameDefinition EffectiveGame, ReelSetDefinition ReelSet, SymbolSetDefinition SymbolSet, PaytableDefinition Paytable,
    SlotProfile FeatureProfile, int PityMissesBefore, long EnergyBalanceBefore, string? FreeSpinFeatureMode,
    int GenerationAttempts, IReadOnlyList<IReadOnlyList<string>> RawGeneratedReels,
    IReadOnlyList<IReadOnlyList<string>> ReelsAfterFreeGameFrequency)
{
    public bool IsDerivedFreeSpin => false;

    public static ClassicSlotRecordedAudit From(ClassicSlotEngineAudit audit) => new(audit.EngineVersion, audit.GeneratedAtUtc,
        audit.BaseGame, audit.EffectiveGame, audit.ReelSet, audit.SymbolSet, audit.Paytable, audit.Profile,
        audit.PityMissesBefore, audit.EnergyBalanceBefore, audit.FeatureMode, audit.GenerationAttempts,
        audit.ReelsBeforeFrequency, audit.ReelsAfterFrequency);

    public ClassicSlotEngineAudit ToEngineAudit() => new(EngineVersion, GeneratedAtUtc, BaseGame, EffectiveGame,
        ReelSet, SymbolSet, Paytable, FeatureProfile, PityMissesBefore, EnergyBalanceBefore, FreeSpinFeatureMode,
        GenerationAttempts, RawGeneratedReels, ReelsAfterFreeGameFrequency);
}

/// <summary>Captures exact index draws from the configured source and replays them without live randomness.</summary>
public static class ClassicSlotReplayEvidence
{
    public static (ClassicSlotSpinOutcome Outcome, byte[] Draws) Record(IGameRandomSource source,
        Func<IGameRandomSource, ClassicSlotSpinOutcome> spin)
    {
        var recording = new DrawRecorder(source);
        var outcome = spin(recording);
        return (outcome, recording.Finish());
    }

    public static ClassicSlotSpinOutcome Reproduce(ClassicSlotSpinOutcome frozen, byte[] draws,
        Func<IGameRandomSource, ClassicSlotSpinOutcome> spin)
    {
        if (frozen.SpinId == Guid.Empty || frozen.Audit.GeneratedAtUtc.Kind != DateTimeKind.Utc || draws is not { Length: > 0 })
            throw InvalidDraws();

        var replay = new DrawReplay(draws);
        var reproduced = spin(replay);
        if (!replay.Finished) throw InvalidDraws();
        return reproduced with
        {
            SpinId = frozen.SpinId,
            Audit = reproduced.Audit with { GeneratedAtUtc = frozen.Audit.GeneratedAtUtc }
        };
    }

    // The caller supplies the same bounds during capture and replay. Small domains use one byte.
    private static int DrawWidth(int bound) => bound switch
    {
        >= 1 and <= 256 => 1,
        > 256 and <= 65_536 => 2,
        _ => throw InvalidDraws()
    };

    private static InvalidOperationException InvalidDraws() => new("The recorded slot draws do not reproduce the prepared outcome.");

    private sealed class DrawRecorder(IGameRandomSource source) : IGameRandomSource
    {
        private readonly List<byte> draws = [];

        public int Next(int maximumExclusive)
        {
            var width = DrawWidth(maximumExclusive);
            var value = source.Next(maximumExclusive);
            if ((uint)value >= (uint)maximumExclusive) throw InvalidDraws();
            draws.Add((byte)(value & 0xFF));
            if (width == 2) draws.Add((byte)(value >> 8));
            return value;
        }

        internal byte[] Finish() => draws.ToArray();
    }

    private sealed class DrawReplay(byte[] draws) : IGameRandomSource
    {
        private int offset;
        internal bool Finished => offset == draws.Length;

        public int Next(int maximumExclusive)
        {
            var width = DrawWidth(maximumExclusive);
            if (offset > draws.Length - width) throw InvalidDraws();
            var value = (int)draws[offset++];
            if (width == 2) value |= draws[offset++] << 8;
            if (value >= maximumExclusive) throw InvalidDraws();
            return value;
        }
    }
}
