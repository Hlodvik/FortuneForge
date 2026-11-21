// Edited: 2026-11-18T18:14:30-06:00 | Computer: Vinnabok
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.ArcaneArchives;

/// <summary>Produces sealed outcomes for the legacy host-admission protocol.</summary>
public sealed class ArcanePaidSlotOutcomes(IGameRandomSource random)
{
    public const string OutcomeKind = "arcane-archives";

    public PaidSlotOutcomeResponse Resolve(PaidSlotOutcomeRequest request)
    {
        var arcane = PaidArcaneSpinOptions.Read(request);
        if (request.MathModelId is not null || !request.IsFreeSpin && request.FreeSpinFeatureMode is not null)
            throw new ArgumentException("The Arcane admission uses an unsupported feature.", nameof(request));

        if (arcane is null)
            return PaidSlotOutcomeResponse.Create(request, PaidSlotOutcomeProtocol.ClassicSlotKind,
                LegacyArcaneWorkerEngine.Spin(random, request));

        if (!ArcaneArchivesEngine.SupportsPotionEngineVersion(arcane.EngineVersion) || request.SpecialBoostApplied ||
            arcane.Potion is not null && (request.IsFreeSpin || arcane.Potion.Id <= 0))
            throw new ArgumentException("A valid versioned Arcane admission is required.", nameof(request));

        var potion = arcane.Potion is { } active
            ? new ArcanePotionBottle(active.Id, active.IngredientId, active.Potency) { VariantId = active.VariantId } : null;
        var outcome = new ArcaneArchivesEngine(random, arcane.EngineVersion).SpinTome(request.WagerPoints, request.PlayerId,
            request.EnergyBalance, request.PityMissesBefore, request.IsFreeSpin, arcane.TomeActive, potion);
        return PaidSlotOutcomeResponse.Create(request, OutcomeKind, outcome);
    }
}
