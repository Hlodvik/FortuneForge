using System.Text.Json;
using System.Text.Json.Serialization;

namespace FortuneForge.Games.NeonNights;

public sealed record NeonNitroChoiceOffer(
    Guid SpinId,
    string SourceKind,
    long WagerPoints,
    int FreeSpinsRemaining,
    IReadOnlyList<IReadOnlyList<string>> InitialReels,
    IReadOnlyList<int> EligibleReels,
    NeonNitroWildOutcome NeonNitroWild)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ReelIndex { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? RoundWinPoints { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? WalletBalanceAfterWagerCents { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SelectedReel { get; init; }

    public bool Allows(Guid spinId, string sourceKind, int reelIndex) =>
        SpinId == spinId && SourceKind == sourceKind && EligibleReels.Contains(reelIndex);
}

public static class NeonNitroChoice
{
    public const string SpinSource = "spin";
    public const string CarRespinSource = "car-respin";

    private static readonly IReadOnlyList<int> ChoiceReels = Array.AsReadOnly(new[] { 0, 1, 2, 3, 4 });

    public static bool IsChoiceReel(int reelIndex) => ChoiceReels.Contains(reelIndex);

    public static NeonNitroChoiceOffer CreateSpinOffer(Guid spinId, long wagerPoints, int freeSpinsRemaining,
        NeonNitroWildOutcome nitro) => CreateOffer(spinId, SpinSource, wagerPoints, freeSpinsRemaining, nitro);

    public static NeonNitroChoiceOffer CreateCarRespinOffer(NeonSpinOutcome original, NeonCarRespinOutcome pending,
        int freeSpinsRemaining, long roundWinPoints) =>
        CreateOffer(original.SpinId, CarRespinSource, original.WagerPoints, freeSpinsRemaining,
            pending.NeonNitroWild) with { ReelIndex = pending.ReelIndex, RoundWinPoints = roundWinPoints };

    private static NeonNitroChoiceOffer CreateOffer(Guid spinId, string sourceKind, long wagerPoints,
        int freeSpinsRemaining, NeonNitroWildOutcome? nitro)
    {
        if (nitro is not { RequiresChoice: true, SecondNitro: not null, ExpandedReel: null })
            throw new InvalidOperationException("The Nitro outcome has no unresolved reel choice.");
        return new(spinId, sourceKind, wagerPoints, freeSpinsRemaining, nitro.InitialReels, ChoiceReels, nitro);
    }

    public static NeonNitroChoiceOffer? ReadOffer(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        var offer = JsonSerializer.Deserialize<NeonNitroChoiceOffer>(json);
        if (offer is not { SpinId: var spinId, WagerPoints: > 0, FreeSpinsRemaining: >= 0,
                NeonNitroWild: { RequiresChoice: true, SecondNitro: not null, ExpandedReel: null } } ||
            spinId == Guid.Empty || offer.InitialReels is null || offer.EligibleReels is null ||
            !offer.EligibleReels.SequenceEqual(ChoiceReels) ||
            !MatchesReels(offer.InitialReels, offer.NeonNitroWild.InitialReels) ||
            offer.WalletBalanceAfterWagerCents < 0 ||
            offer.SelectedReel is { } selected && (!IsChoiceReel(selected) || offer.WalletBalanceAfterWagerCents is null) ||
            offer.SourceKind != SpinSource && offer.WalletBalanceAfterWagerCents is not null ||
            !ValidSource(offer))
            throw new InvalidOperationException("The saved Nitro reel choice is invalid.");
        return offer;
    }

    public static NeonSpinOutcome ResolveSavedSpin(string json, Guid spinId, long wagerPoints, int selectedReel,
        NeonNitroChoiceOffer? offer)
    {
        var pending = JsonSerializer.Deserialize<NeonSpinOutcome>(json)
            ?? throw new InvalidOperationException("The saved Nitro outcome is invalid.");
        if (pending.SpinId != spinId || pending.GameId != NeonArcadeSlotDefinitions.GameId ||
            pending.WagerPoints != wagerPoints ||
            !NeonCarRoundState.IsCarPlay(pending.IsFreeSpin, pending.EngineAudit.FreeSpinFeatureMode) ||
            offer is not null && (!offer.Allows(spinId, SpinSource, selectedReel) ||
                !MatchesOutcome(offer, pending.WagerPoints, pending.NeonNitroWild)))
            throw new InvalidOperationException("The saved Nitro spin differs from its admission.");

        return NeonNightsEngine.ResolveNitroChoice(pending, selectedReel);
    }

    public static NeonCarRespinOutcome ReadSavedCarRespin(string json, NeonSpinOutcome original,
        NeonNitroChoiceOffer offer)
    {
        var pending = JsonSerializer.Deserialize<NeonCarRespinOutcome>(json)
            ?? throw new InvalidOperationException("The saved Car respin Nitro outcome is invalid.");
        if (offer.SourceKind != CarRespinSource || offer.SpinId != original.SpinId ||
            offer.ReelIndex != pending.ReelIndex || !MatchesOutcome(offer, original.WagerPoints, pending.NeonNitroWild) ||
            NeonCarRespin.Check(original, pending).Count != 0)
            throw new InvalidOperationException("The saved Nitro respin differs from its offer.");
        return pending;
    }

    private static bool ValidSource(NeonNitroChoiceOffer offer) => offer.SourceKind switch
    {
        SpinSource => offer.ReelIndex is null,
        CarRespinSource => offer.ReelIndex is { } reel && IsChoiceReel(reel),
        _ => false
    };

    private static bool MatchesOutcome(NeonNitroChoiceOffer offer, long wagerPoints, NeonNitroWildOutcome? nitro) =>
        offer.WagerPoints == wagerPoints && JsonSerializer.Serialize(offer.NeonNitroWild) == JsonSerializer.Serialize(nitro);

    private static bool MatchesReels(IReadOnlyList<IReadOnlyList<string>> first,
        IReadOnlyList<IReadOnlyList<string>>? second) => second is not null &&
        first.Count == second.Count && first.Zip(second).All(pair =>
            pair.First is not null && pair.Second is not null && pair.First.SequenceEqual(pair.Second));
}
