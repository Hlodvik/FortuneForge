using System.Security.Cryptography;
using FortuneForge.Games.Slots;
using Rules = FortuneForge.Games.JadeDragon.JadeDragonCascadeRules;

namespace FortuneForge.Games.JadeDragon;

public sealed record JadeDragonCascadeSpin(Guid SpinId, DateTime GeneratedAtUtc,
    JadeDragonCascadeOutcome Outcome, IReadOnlyList<int> DrawIndexes);

/// <summary>Owns Jade Dragon cascade generation and deterministic replay.</summary>
public sealed class JadeDragonCascadeEngine(IGameRandomSource? random = null)
{
    public static GameDefinition CreateDefinition() => new()
    {
        Id = JadeDragonEngine.GameId,
        Layout = new() { ReelCount = Rules.ReelCount, VisibleRows = Rules.RowCount },
        Symbols = new() { SymbolSetId = Rules.Version, WildSymbolId = string.Empty },
        Matching = new() { MinimumRunLength = Rules.MinimumMatchLength },
        Math = new()
        {
            ReelSetId = Rules.Version, PaytableId = Rules.Version,
            PayoutMultiplier = Rules.PayoutScale, Targets = new()
        },
        Wagering = new()
        {
            PointValueInCents = Rules.WagerPointValueInCents,
            MinimumWagerPoints = Rules.MinimumWagerPoints,
            MaximumWagerPoints = Rules.MaximumWagerPoints,
            WagerIncrementPoints = Rules.WagerIncrementPoints
        },
        FreeGames = new()
        {
            SymbolId = Rules.PearlSymbolId,
            RequiredSymbols = Rules.RequiredPearls,
            AwardedSpins = Rules.BaseFreeSpins
        }
    };

    public static SymbolSetDefinition CreateSymbolSet() => new()
    {
        Id = Rules.Version,
        Symbols = Rules.Symbols.Select(symbol => new SymbolDefinition { Id = symbol.Id }).ToList()
    };

    // A cascade draws independently from these weighted pools. It has no reel stops.
    public static ReelSetDefinition CreateReelSet() => new()
    {
        Id = Rules.Version, SymbolSetId = Rules.Version,
        Reels = Enumerable.Range(0, Rules.ReelCount).Select(_ => Rules.CopyDrawPool()).ToList()
    };

    // The legacy audit envelope stores integral rule multipliers. Store the exact
    // source pay values in hundredths for the versioned cascade snapshot.
    public static PaytableDefinition CreatePaytable() => new()
    {
        Id = Rules.Version, SymbolSetId = Rules.Version,
        Rules = Rules.Symbols.Where(symbol => symbol.Pays is not null)
            .SelectMany(symbol => symbol.Pays!.Select((pay, index) => new PayoutRule
            {
                SymbolId = symbol.Id,
                MatchLength = Rules.MinimumMatchLength + index,
                Multiplier = decimal.ToInt64(pay * 100m)
            })).ToList()
    };

    public JadeDragonCascadeSpin Spin(long wagerPoints, bool isFreeSpin)
    {
        var draws = new List<int>();
        var outcome = Rules.Resolve(wagerPoints, isFreeSpin, upperBound =>
        {
            var index = random?.Next(upperBound) ?? RandomNumberGenerator.GetInt32(upperBound);
            draws.Add(index);
            return index;
        });
        return new(Guid.NewGuid(), DateTime.UtcNow, outcome, draws.ToArray());
    }

    public static JadeDragonCascadeOutcome Replay(long wagerPoints, bool isFreeSpin,
        IReadOnlyList<int> drawIndexes)
    {
        ArgumentNullException.ThrowIfNull(drawIndexes);
        if (drawIndexes.Count < Rules.ReelCount * Rules.RowCount || drawIndexes.Count > Rules.MaximumDraws)
            throw new ArgumentException("The Jade Dragon draw sequence has an invalid length.", nameof(drawIndexes));

        var cursor = 0;
        var outcome = Rules.Resolve(wagerPoints, isFreeSpin, _ =>
            cursor < drawIndexes.Count
                ? drawIndexes[cursor++]
                : throw new ArgumentException("The Jade Dragon draw sequence is incomplete.", nameof(drawIndexes)));
        if (cursor != drawIndexes.Count)
            throw new ArgumentException("The Jade Dragon draw sequence has unused draws.", nameof(drawIndexes));
        return outcome;
    }
}
