using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;

namespace FortuneForge.Games.CrookedRoad.Server;

public static class CrookedRoadPaidRouteContract
{
    public const string SpinPath = "/api/slots/spins";
    public const string StatePath = "/api/slots/crooked-road/state";
    public const string ActionPath = "/api/slots/crooked-road/actions";
    public const string AuditGameId = "crooked-road";
}

public sealed record CrookedRoadRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
    : IClassicSlotRoundRequest;
public sealed record CrookedRoadSpinCommand(string GameId, long WagerPoints, bool UseFreeSpin = false, bool UseSpecialBoost = false);

public sealed record CrookedRoadStateReply(CrookedRoadView GameFeatureState, bool FeatureActionRequired,
    int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    decimal? SlotsCreditsBalance, CrookedRoadRoundRequest? PendingOperation = null)
{
    public int SpecialPointsBalance => 0;
    public long EnergyBalance => 0;
    public IReadOnlyList<object> SealCollections => [];
    public object? PaidMode => null;
    internal static CrookedRoadStateReply From(CrookedRoadSavedState state, long? cents, CrookedRoadRoundRequest? pending = null) =>
        new(CrookedRoadViews.Public(state), CrookedRoadViews.ActionRequired(state), CrookedRoadViews.FreeSpins(state),
            state.ActiveBonus?.FundedWagerPoints, state.ActiveBonus?.Kind, cents / 100m, pending);
}

public sealed record CrookedRoadSpinResponse(Guid SpinId, string GameId, string ReelSetId, string SymbolSetId,
    string PaytableId, long WagerPoints, decimal PointValueInCents, int[] ReelStops, string[][] Reels,
    SpinPayout Payout, bool IsFreeSpin, long ChargedWagerCents, decimal? SlotsCreditsBalance,
    CrookedRoadView GameFeatureState, bool FeatureActionRequired, CrookedRoadFeatureEvent? SpinFeature,
    int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode)
{
    public int ConsecutiveFiveMisses => 0;
    public bool FiveMatchPityTriggered => false;
    public int FreeSpinsAwarded => 0;
    public int CollectionFreeSpinsAwarded => 0;
    public int SpecialPointsAwarded => 0;
    public int SpecialPointsBalance => 0;
    public int EnergyAwarded => 0;
    public long EnergyBalance => 0;
    public bool EnergyMultiplierApplied => false;
    public decimal PayoutMultiplier => 1m;
    public int MonkeyPawCount => 0;
    public long MoneyGrabPoints => 0;
    public long BananaBonusPoints => 0;
    public bool SpecialBoostApplied => false;
    public IReadOnlyDictionary<string, int> SealsAwarded => new Dictionary<string, int>();
    public IReadOnlyList<object> SealCollections => [];
    public object? PaidMode => null;
    internal static CrookedRoadSpinResponse From(CrookedRoadOutcome outcome, long charged, long? cents) =>
        new(outcome.SpinId, CrookedRoadRules.GameId, CrookedRoadRules.ReelSetId, CrookedRoadRules.SymbolSetId,
            CrookedRoadRules.Data.ModelId, outcome.WagerPoints, CrookedRoadRules.PointValueInCents, [0, 0, 0, 0, 0],
            outcome.Reels, outcome.Payout, outcome.Kind == "bonus", charged, cents / 100m,
            CrookedRoadViews.Public(outcome.After), CrookedRoadViews.ActionRequired(outcome.After), outcome.SpinFeature,
            CrookedRoadViews.FreeSpins(outcome.After), outcome.After.ActiveBonus?.FundedWagerPoints, outcome.After.ActiveBonus?.Kind);
}

internal sealed record CrookedRoadPreparedRound(Guid AdmissionId, string AccountId, CrookedRoadRoundRequest Request,
    ClassicSlotRoundState StateBefore, ClassicSlotRoundState StateAfter, CrookedRoadOutcome Outcome,
    CrookedRoadDraw[] Draws, string OutcomeSha256, long WagerCents, long PayoutCents, DateTime StartedAtUtc,
    GameRequestObservation? Observation, bool ImportedLegacyState = false) : IClassicSlotPreparedRound<CrookedRoadRoundRequest>
{
    public Guid SpinId => Outcome.SpinId;
    public DateTime GeneratedAtUtc => Outcome.GeneratedAtUtc;
    public bool SpendsLegacy => false;
}

public sealed record CrookedRoadRoundResult(CrookedRoadStateReply? Features, GameWalletSettlementReceipt Wallet,
    long ChargedWagerCents) : IClassicSlotRoundResult
{
    public bool Settled => Features is not null;
    internal CrookedRoadPreparedRound? Round { get; init; }
}
