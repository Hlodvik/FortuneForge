using System.Text.Json.Serialization;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.GoldFoundry.Server;

/// <summary>Preserves the public Gold Foundry slot result and its separate Crucible cent award.</summary>
public sealed record GoldFoundrySpinResponse(
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
    public int FreeSpinsRemaining => 0;
    public long? FreeSpinWagerPoints => null;
    public int SpecialPointsBalance => 0;
    public long EnergyBalance { get; init; }
    public bool EnergyMultiplierApplied => false;
    public decimal PayoutMultiplier => 1m;
    public int MonkeyPawCount { get; init; }
    public long MoneyGrabPoints { get; init; }
    public long BananaBonusPoints { get; init; }
    public IReadOnlyDictionary<string, int> SealsAwarded { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyList<object> SealCollections => [];
    public int CollectionFreeSpinsAwarded => 0;
    public string? FreeSpinFeatureMode => null;
    public object? BonusRound => null;
    public IReadOnlyList<object> QueuedBonusRounds => [];
    public IReadOnlyList<object> AwardedBonusRounds => [];
    public object? MidnightBonusState => null;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CrucibleWageredCents { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CruciblePourValueCents { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CruciblePoured { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CruciblePayoutCents { get; init; }

    public static GoldFoundrySpinResponse From(GoldFoundryRoundResult result)
    {
        if (result.Round is not { } round || result.Features is not { } features)
            throw new InvalidOperationException("The Gold Foundry round has no settled spin response.");

        return Capture(round) with
        {
            ChargedWagerCents = 0,
            SlotsCreditsBalance = result.Wallet.BalanceAfterCents / 100m,
            EnergyBalance = features.EnergyBalance,
            CrucibleWageredCents = features.CrucibleWageredCents,
            CruciblePourValueCents = features.CruciblePourValueCents,
            CruciblePoured = round.Crucible.Poured,
            CruciblePayoutCents = round.Crucible.PayoutCents
        };
    }

    internal static GoldFoundrySpinResponse Capture(GoldFoundryPreparedRound round)
    {
        var spin = round.Outcome;
        return new(spin.SpinId, spin.GameId, spin.ReelSetId, spin.SymbolSetId, spin.PaytableId,
            spin.WagerPoints, spin.PointValueInCents, spin.ReelStops, spin.Reels, spin.Payout,
            spin.ConsecutiveFiveMisses, spin.FiveMatchPityTriggered, spin.FreeSpinsAwarded,
            spin.SpecialPointsAwarded, spin.EnergyAwarded, spin.SpecialBoostApplied)
        {
            IsFreeSpin = spin.IsFreeSpin,
            ChargedWagerCents = round.WagerCents,
            MonkeyPawCount = spin.CollectorCount,
            MoneyGrabPoints = spin.CollectorPayoutPoints,
            BananaBonusPoints = spin.HammerBonusPoints,
            SealsAwarded = spin.SealsAwarded
        };
    }

    internal GoldFoundrySpinOutcome ToOutcome(GoldFoundryEngineAudit audit) =>
        new(SpinId, GameId, ReelSetId, SymbolSetId, PaytableId, WagerPoints, PointValueInCents, ReelStops, Reels,
            Payout, ConsecutiveFiveMisses, FiveMatchPityTriggered, FreeSpinsAwarded, SpecialPointsAwarded, EnergyAwarded,
            SpecialBoostApplied, IsFreeSpin, MonkeyPawCount, MoneyGrabPoints, BananaBonusPoints, SealsAwarded, audit);
}
