using System.Text.Json.Serialization;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights.Server;

public sealed record NeonRoundRequest(string OperationId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost = false)
{
    public static bool IsValidOperationId(string? value) => value is { Length: >= 8 and <= 128 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record NeonRoundStateReply(int FreeSpinsRemaining, long? FreeSpinWagerPoints, string? FreeSpinFeatureMode,
    int NeonDiscoSpinsRemaining, long? NeonDiscoWagerPoints, int? NeonDiscoMultiplier, long? NeonDiscoRoundWinPoints,
    NeonCarRespinOffer? NeonCarRespinOffer, long? NeonCarRoundWinPoints, NeonNitroChoiceOffer? PendingNeonNitroChoice,
    NeonRoundRequest? PendingOperation)
{
    public int SpecialPointsBalance => 0;
    public long EnergyBalance => 0;
    public IReadOnlyList<object> SealCollections => [];
    public decimal? SlotsCreditsBalance { get; init; }

    internal static NeonRoundStateReply Create(NeonAwardedPlayState awarded, NeonDiscoRoundState disco,
        NeonCarRoundState car, NeonNitroChoiceOffer? nitro = null, NeonRoundRequest? pending = null,
        bool consumedDisco = false, bool consumedCar = false) => new(checked((int)awarded.Remaining),
        awarded.LockedWagerPoints, awarded.FeatureMode, disco.SpinsRemaining, disco.LockedWagerPoints,
        disco.Multiplier, disco.ReceiptWinPoints(consumedDisco), nitro is null ? car.AvailableOffer() : null,
        consumedCar ? car.WinPoints : car.ActiveWinPoints(awarded.FeatureMode), nitro, pending);
}

public sealed record NeonSpinReply(Guid SpinId, string GameId, string ReelSetId, string SymbolSetId, string PaytableId,
    long WagerPoints, decimal PointValueInCents, IReadOnlyList<int> ReelStops,
    IReadOnlyList<IReadOnlyList<string>> Reels, SpinPayout Payout, int ConsecutiveFiveMisses, bool FiveMatchPityTriggered,
    int FreeSpinsAwarded, int SpecialPointsAwarded, int EnergyAwarded, bool SpecialBoostApplied,
    bool IsFreeSpin, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] long ChargedWagerCents,
    decimal SlotsCreditsBalance, long BananaBonusPoints, NeonArcadeEvidence NeonArcade,
    NeonDiscoCascadeOutcome? NeonDiscoCascade, NeonNitroWildOutcome? NeonNitroWild, int FreeSpinsRemaining,
    long? FreeSpinWagerPoints, string? FreeSpinFeatureMode, int NeonDiscoSpinsRemaining, long? NeonDiscoWagerPoints,
    int? NeonDiscoMultiplier, long? NeonDiscoRoundWinPoints, NeonCarRespinOffer? NeonCarRespinOffer,
    long? NeonCarRoundWinPoints)
{
    public int SpecialPointsBalance => 0;
    public long EnergyBalance => 0;
    public decimal PayoutMultiplier => 1m;

    internal static NeonSpinReply Create(NeonSpinOutcome outcome, long wagerCents, long balanceCents,
        NeonRoundStateReply state) => new(outcome.SpinId, outcome.GameId, outcome.ReelSetId, outcome.SymbolSetId,
        outcome.PaytableId, outcome.WagerPoints, outcome.PointValueInCents, outcome.ReelStops, outcome.Reels,
        outcome.Payout, outcome.ConsecutiveFiveMisses, outcome.FiveMatchPityTriggered, outcome.FreeSpinsAwarded,
        outcome.SpecialPointsAwarded, outcome.EnergyAwarded, outcome.SpecialBoostApplied, outcome.IsFreeSpin,
        wagerCents, balanceCents / 100m, outcome.BananaBonusPoints, outcome.NeonArcade, outcome.NeonDiscoCascade,
        outcome.NeonNitroWild, state.FreeSpinsRemaining, state.FreeSpinWagerPoints, state.FreeSpinFeatureMode,
        state.NeonDiscoSpinsRemaining, state.NeonDiscoWagerPoints, state.NeonDiscoMultiplier,
        state.NeonDiscoRoundWinPoints, state.NeonCarRespinOffer, state.NeonCarRoundWinPoints);
}

public sealed record NeonRoundResult(NeonSpinReply? Spin, NeonNitroChoiceOffer? PendingNeonNitroChoice,
    GameWalletSettlementReceipt? Wallet)
{
    public long ChargedWagerCents { get; init; }
}

public sealed class NeonPrimaryRoundConflictException(string message, NeonRoundRequest? pendingRequest = null)
    : InvalidOperationException(message)
{
    public NeonRoundRequest? PendingRequest { get; } = pendingRequest;
}

public sealed class NeonPrimaryNitroConflictException(string message) : InvalidOperationException(message);
public sealed class NeonLegacyNitroChoiceException() : InvalidOperationException("The legacy worker owns this Nitro choice.");
public sealed class NeonPrimaryRoundPausedException() : InvalidOperationException("New Neon rounds are temporarily paused.");
public sealed class NeonPrimaryFreeSpinsException() : InvalidOperationException("No Neon special plays are available.");
public sealed class NeonPrimaryFreeSpinsRequiredException() : InvalidOperationException("Finish the current Neon Special Plays first.");
public sealed class NeonPrimaryRoundCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous spin to finish.")
{
    public TimeSpan Remaining { get; } = remaining;
}
