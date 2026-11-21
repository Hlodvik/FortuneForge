using System.Text.Json.Serialization;
using FortuneForge.Games.Slots;
using PotionRules = FortuneForge.Games.ArcaneArchives.ArcanePotions;
using TomeRules = FortuneForge.Games.ArcaneArchives.ArcaneTome;

namespace FortuneForge.Games.ArcaneArchives.Server;

/// <summary>Public Arcane response with the established slot transport fields.</summary>
public sealed record ArcaneSpinResponse(
    Guid SpinId, string GameId, string ReelSetId, string SymbolSetId, string PaytableId,
    long WagerPoints, decimal PointValueInCents, IReadOnlyList<int> ReelStops,
    IReadOnlyList<IReadOnlyList<string>> Reels, SpinPayout Payout,
    int ConsecutiveFiveMisses, bool FiveMatchPityTriggered, int FreeSpinsAwarded,
    int SpecialPointsAwarded, int EnergyAwarded, bool SpecialBoostApplied)
{
    public decimal? SlotsCreditsBalance { get; init; }
    public bool IsFreeSpin { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long ChargedWagerCents { get; init; }
    public int NeonDiscoSpinsRemaining => 0;
    public int FreeSpinsRemaining { get; init; }
    public long? FreeSpinWagerPoints { get; init; }
    public int SpecialPointsBalance => 0;
    public long EnergyBalance => 0;
    public bool EnergyMultiplierApplied => false;
    public decimal PayoutMultiplier => 1m;
    public int MonkeyPawCount => 0;
    public long MoneyGrabPoints => 0;
    public long BananaBonusPoints => 0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArcaneWandCollection? ArcaneWandCollection { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArcaneTomeView? ArcaneTome { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ArcanePotionView? ArcanePotions { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, long>? ArcaneIngredientsAwarded { get; init; }
    public IReadOnlyDictionary<string, int> SealsAwarded { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyList<object> SealCollections => [];
    public int CollectionFreeSpinsAwarded => 0;
    public string? FreeSpinFeatureMode { get; init; }
    public object? BonusRound => null;
    public IReadOnlyList<object> QueuedBonusRounds => [];
    public IReadOnlyList<object> AwardedBonusRounds => [];
    public object? MidnightBonusState => null;

    public static ArcaneSpinResponse From(ArcaneRoundResult result)
    {
        if (result.Round is not { } round || result.Features is not { } features)
            throw new InvalidOperationException("The Arcane round has no settled spin response.");

        return From(round, features, result.SlotsCreditsBalance);
    }

    public static ArcaneSpinResponse From(ArcanePreparedRound round, ArcaneFeatureSettlement features, decimal walletBalance) =>
        CaptureOutcome(round) with
        {
            ChargedWagerCents = 0,
            SlotsCreditsBalance = walletBalance,
            FreeSpinsRemaining = round.FreeSpinsRemaining,
            FreeSpinWagerPoints = round.FreeSpinWagerPoints,
            FreeSpinFeatureMode = round.FreeSpinFeatureMode,
            ArcaneTome = TomeRules.ToView(features.Tome?.State),
            ArcanePotions = features.Potions is { } potions ? ArcanePotionView.FromState(potions) : null,
            ArcaneIngredientsAwarded = features.Tome?.IngredientsAwarded
        };

    internal static ArcaneSpinResponse CaptureOutcome(ArcanePreparedRound round)
    {
        var outcome = round.Outcome;
        var potion = round.Features.Potions is { } potions ? PotionRules.ForSpin(potions, outcome.IsFreeSpin) : null;
        return new(outcome.SpinId, outcome.GameId, outcome.ReelSetId, outcome.SymbolSetId, outcome.PaytableId,
            outcome.WagerPoints, outcome.PointValueInCents, outcome.ReelStops, outcome.Reels,
            outcome.Payout, outcome.ConsecutiveFiveMisses, outcome.FiveMatchPityTriggered,
            outcome.FreeSpinsAwarded, outcome.SpecialPointsAwarded, outcome.EnergyAwarded, outcome.SpecialBoostApplied)
        {
            IsFreeSpin = outcome.IsFreeSpin,
            ChargedWagerCents = round.WagerCents,
            ArcaneWandCollection = ArcaneWandCollector.Calculate(outcome.Reels, outcome.WagerPoints, potion),
            SealsAwarded = outcome.CollectionsAwarded
        };
    }
}
