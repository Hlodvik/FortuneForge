using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Spins;

public sealed partial class SpinService
{
    /// <summary>
    /// Explicit nonfinancial entry used by the loopback fixture host. No production
    /// controller, account store, wallet, earning or consumption path calls this.
    /// </summary>
    public SpinResult SpinWukongPawPrototype(WukongPawPrototypeAdmission admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        var evidence = new WukongPawPrototypeEvidence(
            admission.SessionId, admission.AdmissionId ?? string.Empty, admission.GrantId,
            admission.WagerPoints, admission.ForcedPawColumn, admission.InjectEarnSymbol);
        return SpinCore(SlotSpecialRoundProfiles.WukongGameId, admission.WagerPoints,
            $"synthetic-paw-preview:{admission.SessionId}", false, 0, null, null, false, null, evidence);
    }

    private static void ValidateWukongPawPrototype(
        GameDefinition game, long wagerPoints, WukongPawPrototypeEvidence evidence)
    {
        if (game.Id != SlotSpecialRoundProfiles.WukongGameId || !evidence.SyntheticOnly ||
            evidence.CollectionRule != WukongPawPrototypeEvidence.RuleVersion ||
            string.IsNullOrWhiteSpace(evidence.SessionId) || string.IsNullOrWhiteSpace(evidence.AdmissionId) ||
            evidence.WagerPoints != wagerPoints ||
            evidence.ForcedPawColumn is { } column &&
                (column < 0 || column >= game.Layout.ReelCount || string.IsNullOrWhiteSpace(evidence.GrantId)))
            throw new ArgumentException("Invalid synthetic Wukong paw admission.");
    }

    private static ReelOutcome ApplyWukongPawPrototype(ReelOutcome outcome, WukongPawPrototypeEvidence evidence)
    {
        var reels = outcome.VisibleReels.Select(reel => reel.ToArray()).ToArray();
        // Illustrative fixture injection has no configured earning probability or award.
        if (evidence.InjectEarnSymbol)
        {
            var earnColumn = ((evidence.ForcedPawColumn ?? 0) + 1) % reels.Length;
            reels[earnColumn][^1] = WukongPawPrototypeEvidence.EarnSymbolId;
        }
        // Placement is fixed before admission. It participates in normal evaluation,
        // pity and awards; no settled result is edited after generation.
        if (evidence.ForcedPawColumn is { } column) reels[column][0] = MonkeyPawSymbolId;
        return outcome with { VisibleReels = reels.Select(reel => (IReadOnlyList<string>)reel).ToArray() };
    }
}
