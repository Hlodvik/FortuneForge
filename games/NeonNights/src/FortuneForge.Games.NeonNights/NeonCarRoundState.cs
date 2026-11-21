using System.Text.Json;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NeonNights;

public sealed record NeonCarRespinOffer(
    Guid SpinId,
    IReadOnlyList<int> EligibleReels,
    IReadOnlyList<IReadOnlyList<string>> Reels,
    long RoundWinPoints);

public sealed record NeonCarRoundState(string? RoundId, long WinPoints, string? OfferSpinId, string? OfferJson,
    bool HasActiveAdmission)
{
    private const string RoundField = "neonCarRespinRoundId";
    private const string OfferSpinField = "neonCarRespinSourceSpinId";
    private const string OfferJsonField = "neonCarRespinOfferJson";
    private const string WinsField = "neonCarRoundWinPoints";
    private const string ReceiptIdField = "neonCarRespinReceiptId";
    private const string FrozenSourceField = "neonCarRespinSourceJson";

    public static NeonCarRoundState Inactive { get; } = new(null, 0, null, null, false);

    public static bool IsCarPlay(bool usesAwardedPlay, string? featureMode) =>
        usesAwardedPlay && IsCarMode(featureMode);

    public static NeonCarRoundState Start(string roundId, string? featureMode) =>
        IsCarMode(featureMode) ? Inactive with { RoundId = roundId } : Inactive;

    private static bool IsCarMode(string? featureMode) => featureMode == NeonArcadeBonus.PaidCarFeatureMode;

    public static NeonCarRoundState ReadGuard(Func<string, string?> readText, Func<string, long> readLong) =>
        new(ReadRoundId(readText), readLong(WinsField), readText(OfferSpinField), readText(OfferJsonField),
            readText(SlotSpinGuard.ActiveAdmissionField) is not null);

    public static string? ReadRoundId(Func<string, string?> readText) => readText(RoundField);

    public static (string RoundId, string OutcomeJson, string ReceiptId, bool UsesLegacyRoundReceipt)? ReadFrozenSource(
        Func<string, string?> readText)
    {
        var roundId = ReadRoundId(readText);
        var outcome = readText(FrozenSourceField);
        if (roundId is null || outcome is null) return null;

        var perSpinReceiptId = readText(ReceiptIdField);
        return (roundId, outcome, perSpinReceiptId ?? roundId, perSpinReceiptId is null);
    }

    public long? WinPointsForRound(string? roundId) => roundId is not null && roundId == RoundId ? WinPoints : null;

    public long? ActiveWinPoints(string? featureMode) => IsCarMode(featureMode) ? WinPoints : null;

    public NeonCarRespinOffer? AvailableOffer()
    {
        if (HasActiveAdmission || RoundId is null || OfferSpinId is null || OfferJson is null) return null;

        var offer = JsonSerializer.Deserialize<NeonCarRespinOffer>(OfferJson);
        if (offer is null || offer.EligibleReels.Count == 0 ||
            offer.SpinId.ToString("N") != OfferSpinId || offer.RoundWinPoints != WinPoints)
            throw new InvalidOperationException("The saved Car respin offer is invalid.");
        return offer;
    }

    public NeonCarRespinOffer? OfferForSpin(Guid spinId) =>
        OfferSpinId == spinId.ToString("N") ? AvailableOffer() : null;

    public bool CanRespin(string roundId, Guid spinId, int? reelIndex) =>
        RoundId == roundId && reelIndex is { } reel && OfferForSpin(spinId)?.EligibleReels.Contains(reel) == true;

    public bool CanDismiss(Guid spinId, Func<bool> hasPendingNitroChoice) =>
        !HasActiveAdmission && OfferSpinId == spinId.ToString("N") && !hasPendingNitroChoice();

    public NeonCarRoundState Advance(Guid spinId, NeonSpinOutcome? carSpin, string? nextFeatureMode,
        long settledPayoutPoints)
    {
        var spinKey = spinId.ToString("N");
        if (carSpin is null) return Start(spinKey, nextFeatureMode);

        var wins = checked(WinPoints + settledPayoutPoints);
        var next = new NeonCarRoundState(RoundId ?? spinKey, wins, null, null, false);
        var eligibleReels = settledPayoutPoints == 0 ? NeonCarRespin.OfferReels(carSpin) : [];
        if (eligibleReels.Count == 0) return next;

        var offer = new NeonCarRespinOffer(spinId, eligibleReels, carSpin.Reels, wins);
        return next with { OfferSpinId = spinKey, OfferJson = JsonSerializer.Serialize(offer) };
    }

    public NeonCarRoundState ClearOffer() => this with { OfferSpinId = null, OfferJson = null };

    public NeonCarRoundState CompleteRespin(long improvementPoints) =>
        ClearOffer() with { WinPoints = checked(WinPoints + improvementPoints) };

    public void WriteGuard(IDictionary<string, object> fields)
    {
        fields[RoundField] = RoundId ?? string.Empty;
        WriteRespinGuard(fields);
    }

    public void WriteRespinGuard(IDictionary<string, object> fields)
    {
        fields[WinsField] = WinPoints;
        WriteOffer(fields);
    }

    public void WriteOffer(IDictionary<string, object> fields)
    {
        fields[OfferSpinField] = OfferSpinId ?? string.Empty;
        fields[OfferJsonField] = OfferJson ?? string.Empty;
    }

    public void WriteSpinResult(IDictionary<string, object> fields, NeonSpinOutcome carSpin)
    {
        fields[RoundField] = RoundId!;
        if (OfferSpinId is null) return;

        fields[ReceiptIdField] = OfferSpinId;
        fields[FrozenSourceField] = JsonSerializer.Serialize(carSpin);
    }
}
