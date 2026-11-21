using System.Text.Json.Serialization;
using FortuneForge.Games.Slots;
using LegacySpin = FortuneForge.Games.JadeDragon.Models.SpinResult;

namespace FortuneForge.Games.JadeDragon.Server;

/// <summary>Maps package-owned rounds to Jade Dragon's existing public slot response.</summary>
public sealed record JadeSpinResponse(
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
    public int SpecialPointsBalance { get; init; }
    public long EnergyBalance { get; init; }
    public bool EnergyMultiplierApplied => false;
    public decimal PayoutMultiplier => 1m;
    public int MonkeyPawCount { get; init; }
    public long MoneyGrabPoints { get; init; }
    public long BananaBonusPoints { get; init; }
    public IReadOnlyDictionary<string, int> SealsAwarded { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyList<JadeSealCollection> SealCollections { get; init; } = [];
    public int CollectionFreeSpinsAwarded => 0;
    public string? FreeSpinFeatureMode { get; init; }
    public object? BonusRound => null;
    public IReadOnlyList<object> QueuedBonusRounds => [];
    public IReadOnlyList<object> AwardedBonusRounds => [];
    public object? MidnightBonusState => null;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JadeDragonCascadeOutcome? JadeDragonCascade { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PayoutPointValueInCents { get; init; }

    public static JadeSpinResponse From(JadeRoundResult result)
    {
        if (result.Round is not { } round || result.Features is not { } features)
            throw new InvalidOperationException("The Jade Dragon round has no settled spin response.");

        return Capture(round) with
        {
            Payout = round.SettledPayout,
            SlotsCreditsBalance = result.Wallet.BalanceAfterCents / 100m,
            FreeSpinsRemaining = features.FreeSpinsRemaining,
            FreeSpinWagerPoints = features.FreeSpinWagerPoints,
            FreeSpinFeatureMode = features.FreeSpinFeatureMode,
            SpecialPointsBalance = features.SpecialPointsBalance,
            EnergyBalance = features.EnergyBalance,
            SealCollections = features.SealCollections
        };
    }

    // The private evidence wraps this pre-settlement projection with a separate audit.
    internal static JadeSpinResponse Capture(JadePreparedRound round)
    {
        if (round.Outcome.Cascade is { } cascade)
        {
            var outcome = cascade.Outcome;
            return new(cascade.SpinId, JadeDragonEngine.GameId, JadeDragonCascadeRules.Version,
                JadeDragonCascadeRules.Version, JadeDragonCascadeRules.Version, round.EffectiveWagerPoints,
                JadeDragonCascadeRules.WagerPointValueInCents, [], outcome.InitialReels, new(outcome.TotalPoints, []),
                round.Before.Pity, false, outcome.FreeSpinsAwarded, 0, 0, false)
            {
                IsFreeSpin = round.Request.UseFreeSpin,
                ChargedWagerCents = round.WagerCents,
                JadeDragonCascade = outcome,
                PayoutPointValueInCents = JadeDragonCascadeRules.PayoutPointValueInCents
            };
        }

        return CaptureLegacy(round.Outcome.Legacy ?? throw new InvalidOperationException("Missing saved Jade Dragon outcome."));
    }

    internal static JadeSpinResponse CaptureLegacy(LegacySpin legacy) =>
        new(legacy.SpinId, legacy.GameId, legacy.ReelSetId, legacy.SymbolSetId, legacy.PaytableId,
            legacy.WagerPoints, legacy.PointValueInCents, legacy.ReelStops, legacy.Reels, legacy.Payout,
            legacy.ConsecutiveFiveMisses, legacy.FiveMatchPityTriggered, legacy.FreeSpinsAwarded,
            legacy.SpecialPointsAwarded, legacy.EnergyAwarded, legacy.SpecialBoostApplied)
        {
            IsFreeSpin = legacy.IsFreeSpin,
            MonkeyPawCount = legacy.CollectorCount,
            MoneyGrabPoints = legacy.CollectorPayoutPoints,
            BananaBonusPoints = legacy.PatternPayoutPoints,
            SealsAwarded = legacy.CollectionSymbolsAwarded
        };
}
