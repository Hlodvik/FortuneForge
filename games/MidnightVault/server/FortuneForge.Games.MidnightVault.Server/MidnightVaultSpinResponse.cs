using System.Text.Json.Serialization;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.MidnightVault.Server;

/// <summary>Maps the settled package outcome onto Midnight Vault's public slot response.</summary>
public sealed record MidnightVaultSpinResponse(
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
    public int MonkeyPawCount => 0;
    public long MoneyGrabPoints => 0;
    public long BananaBonusPoints { get; init; }
    public IReadOnlyDictionary<string, int> SealsAwarded { get; init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyList<MidnightVaultSealCollection> SealCollections { get; init; } = [];
    public int CollectionFreeSpinsAwarded => 0;
    public string? FreeSpinFeatureMode { get; init; }
    public MidnightVaultBonusRound? BonusRound { get; init; }
    public IReadOnlyList<MidnightVaultBonusRound> QueuedBonusRounds { get; init; } = [];
    public IReadOnlyList<MidnightVaultBonusRound> AwardedBonusRounds { get; init; } = [];
    public object? MidnightBonusState => null;

    public static MidnightVaultSpinResponse From(MidnightVaultRoundResult result)
    {
        if (result.Round is not { } round || result.Features is not { } features)
            throw new InvalidOperationException("The Midnight Vault round has no settled spin response.");

        return Capture(round) with
        {
            ChargedWagerCents = 0,
            SlotsCreditsBalance = result.Wallet.BalanceAfterCents / 100m,
            FreeSpinsRemaining = features.FreeSpinsRemaining,
            FreeSpinWagerPoints = features.FreeSpinWagerPoints,
            SpecialPointsBalance = features.SpecialPointsBalance,
            EnergyBalance = features.EnergyBalance,
            SealCollections = features.SealCollections,
            FreeSpinFeatureMode = features.FreeSpinFeatureMode,
            BonusRound = features.BonusRound,
            QueuedBonusRounds = features.QueuedBonusRounds,
            AwardedBonusRounds = round.Progress.BonusEvidence.AwardedRounds
        };
    }

    internal static MidnightVaultSpinResponse Capture(MidnightVaultPreparedRound round)
    {
        var spin = round.Outcome;
        return new(spin.SpinId, spin.GameId, spin.ReelSetId, spin.SymbolSetId, spin.PaytableId,
            spin.WagerPoints, spin.PointValueInCents, spin.ReelStops, spin.Reels, spin.Payout,
            spin.ConsecutiveFiveMisses, spin.FiveMatchPityTriggered, spin.FreeSpinsAwarded,
            spin.SpecialPointsAwarded, spin.EnergyAwarded, spin.SpecialBoostApplied)
        {
            IsFreeSpin = spin.IsFreeSpin,
            ChargedWagerCents = round.WagerCents,
            BananaBonusPoints = spin.BananaBonusPoints,
            SealsAwarded = spin.SealsAwarded
        };
    }

    internal MidnightVaultSpinOutcome ToOutcome(MidnightVaultEngineAudit audit) =>
        new(SpinId, GameId, ReelSetId, SymbolSetId, PaytableId, WagerPoints, PointValueInCents, ReelStops, Reels,
            Payout, ConsecutiveFiveMisses, FiveMatchPityTriggered, FreeSpinsAwarded, SpecialPointsAwarded, EnergyAwarded,
            SpecialBoostApplied, IsFreeSpin, BananaBonusPoints, SealsAwarded, audit);
}
