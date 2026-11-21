using System.Text.Json.Serialization;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.GodsOfOlympus.Server;

/// <summary>Projects both Olympus boards onto the established public slot response.</summary>
public sealed record OlympusSpinResponse(
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
    public IReadOnlyList<OlympusSealCollection> SealCollections { get; init; } = [];
    public int CollectionFreeSpinsAwarded => 0;
    public string? FreeSpinFeatureMode { get; init; }
    public object? BonusRound => null;
    public IReadOnlyList<object> QueuedBonusRounds => [];
    public IReadOnlyList<object> AwardedBonusRounds => [];
    public object? MidnightBonusState => null;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OlympusTumbleOutcome? OlympusTumble { get; init; }

    public static OlympusSpinResponse From(OlympusRoundResult result)
    {
        if (result.Round is not { } round || result.Features is not { } features)
            throw new InvalidOperationException("The Olympus round has no settled spin response.");

        return Capture(round) with
        {
            ChargedWagerCents = 0,
            SlotsCreditsBalance = result.Wallet.BalanceAfterCents / 100m,
            FreeSpinsRemaining = features.FreeSpinsRemaining,
            FreeSpinWagerPoints = features.FreeSpinWagerPoints,
            SpecialPointsBalance = features.SpecialPointsBalance,
            EnergyBalance = features.EnergyBalance,
            SealCollections = features.SealCollections,
            FreeSpinFeatureMode = features.FreeSpinFeatureMode
        };
    }

    internal static OlympusSpinResponse Capture(OlympusPreparedRound round)
    {
        if (round.Engine.Tumble is { } tumble)
        {
            var reels = tumble.Outcome.InitialReels;
            return new(tumble.SpinId, tumble.GameId, tumble.EngineVersion, tumble.BaseGame.Symbols.SymbolSetId,
                tumble.EngineVersion, tumble.WagerPoints, tumble.PointValueInCents, new int[reels.Count], reels,
                new(tumble.Outcome.TotalPoints, []), round.Before.Pity, false, 0, 0, 0, false)
            {
                OlympusTumble = tumble.Outcome,
                ChargedWagerCents = round.WagerCents
            };
        }

        var spin = round.Engine.Classic ?? throw new InvalidOperationException("The saved classic Olympus spin is missing.");
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

    internal ClassicSlotSpinOutcome ToClassic(ClassicSlotEngineAudit audit) =>
        new(SpinId, GameId, ReelSetId, SymbolSetId, PaytableId, WagerPoints, PointValueInCents, ReelStops, Reels,
            Payout, ConsecutiveFiveMisses, FiveMatchPityTriggered, FreeSpinsAwarded, SpecialPointsAwarded, EnergyAwarded,
            SpecialBoostApplied, IsFreeSpin, MonkeyPawCount, MoneyGrabPoints, BananaBonusPoints, SealsAwarded, audit);
}
