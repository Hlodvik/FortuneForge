using System.Text.Json.Serialization;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.CosmicFortune.Server;

/// <summary>Maps Cosmic Fortune’ settled slot outcome to the established public response.</summary>
public sealed record CosmicFortuneSpinResponse(
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
    public bool EnergyMultiplierApplied { get; init; }
    public decimal PayoutMultiplier { get; init; } = 1m;
    public int MonkeyPawCount { get; init; }
    public long MoneyGrabPoints { get; init; }
    public long BananaBonusPoints { get; init; }
    public IReadOnlyDictionary<string, int> SealsAwarded { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyList<CosmicFortuneSealCollection> SealCollections { get; init; } = [];
    public int CollectionFreeSpinsAwarded => 0;
    public string? FreeSpinFeatureMode { get; init; }
    public object? BonusRound => null;
    public IReadOnlyList<object> QueuedBonusRounds => [];
    public IReadOnlyList<object> AwardedBonusRounds => [];
    public object? MidnightBonusState => null;

    public static CosmicFortuneSpinResponse From(CosmicFortuneRoundResult result)
    {
        if (result.Round is not { } round || result.Features is not { } features)
            throw new InvalidOperationException("The Cosmic Fortune round has no settled spin response.");

        return Capture(round) with
        {
            ChargedWagerCents = 0,
            Payout = round.Progress.SettledPayout,
            SlotsCreditsBalance = result.Wallet.BalanceAfterCents / 100m,
            FreeSpinsRemaining = features.FreeSpinsRemaining,
            FreeSpinWagerPoints = features.FreeSpinWagerPoints,
            SpecialPointsBalance = features.SpecialPointsBalance,
            EnergyBalance = features.EnergyBalance,
            EnergyMultiplierApplied = round.Progress.EnergySettlement.MultiplierApplied,
            PayoutMultiplier = round.Progress.EnergySettlement.PayoutMultiplier,
            SealCollections = features.SealCollections,
            FreeSpinFeatureMode = features.FreeSpinFeatureMode
        };
    }

    internal static CosmicFortuneSpinResponse Capture(CosmicFortunePreparedRound round)
    {
        var spin = round.Outcome;
        return new(spin.SpinId, spin.GameId, spin.ReelSetId, spin.SymbolSetId, spin.PaytableId,
            spin.WagerPoints, spin.PointValueInCents, spin.ReelStops, spin.Reels, spin.Payout,
            spin.ConsecutiveFiveMisses, spin.FiveMatchPityTriggered, spin.FreeSpinsAwarded,
            spin.SpecialPointsAwarded, spin.EnergyAwarded, spin.SpecialBoostApplied)
        {
            IsFreeSpin = spin.IsFreeSpin,
            ChargedWagerCents = round.WagerCents,
            MonkeyPawCount = spin.PawCount,
            MoneyGrabPoints = spin.CollectorPoints,
            BananaBonusPoints = spin.PatternBonusPoints,
            SealsAwarded = spin.CollectionsAwarded
        };
    }

    internal ClassicSlotSpinOutcome ToOutcome(ClassicSlotEngineAudit audit) =>
        new(SpinId, GameId, ReelSetId, SymbolSetId, PaytableId, WagerPoints, PointValueInCents, ReelStops, Reels,
            Payout, ConsecutiveFiveMisses, FiveMatchPityTriggered, FreeSpinsAwarded, SpecialPointsAwarded, EnergyAwarded,
            SpecialBoostApplied, IsFreeSpin, MonkeyPawCount, MoneyGrabPoints, BananaBonusPoints, SealsAwarded, audit);
}
