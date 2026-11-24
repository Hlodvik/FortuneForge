using FortuneForge.Games.Slots;
using System.Text.Json.Serialization;

namespace FortuneForge.Games.CosmicFortune.Server;

public sealed record PharaohsAscentScarabTrail(IReadOnlyList<GridPosition> Positions,
    int SymbolCount, long Multiplier, long AmountPoints);

public sealed record PharaohsAscentFeature(IReadOnlyList<PharaohsAscentScarabTrail> ScarabTrails,
    long ScarabTrailPoints)
{
    public string Kind => "pharaoh-ascent";

    internal static PharaohsAscentFeature? From(CosmicFortunePreparedRound round)
    {
        if (round.Outcome.Audit.EngineVersion == CosmicFortuneEngine.LegacyEngineVersion) return null;
        var trails = round.Progress.SettledPayout.Paylines
            .Where(line => line.PaylineId >= PharaohsAscentScarabs.FirstPaylineId &&
                line.Matches.Count == 1 && line.Matches[0].Match.SymbolId == PharaohsAscentScarabs.SymbolId)
            .Select(line => new PharaohsAscentScarabTrail(line.Matches[0].Match.Positions,
                line.Matches[0].Match.MatchLength, line.Matches[0].Multiplier, line.AmountPoints)).ToArray();
        return new(trails, trails.Sum(trail => trail.AmountPoints));
    }
}

/// <summary>The persisted, wallet-settled chamber progress exposed to the game-owned panel.</summary>
public sealed record PharaohsAscentState(IReadOnlyList<CosmicFortuneSealCollection> Collections,
    int FreeSpinsRemaining, string? FreeSpinFeatureMode, long? FreeSpinWagerPoints, long EnergyBalance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SealedTombView? Tomb = null)
{
    public string Kind => "pharaoh-ascent";
}
