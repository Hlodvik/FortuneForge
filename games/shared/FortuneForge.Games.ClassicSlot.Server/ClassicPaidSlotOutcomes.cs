// Edited: 2026-11-18T18:14:04-06:00 | Computer: Vinnabok
using FortuneForge.Games.Slots;
#if ARCANE_ARCHIVES
using FortuneForge.Games.ArcaneArchives;
#endif
#if OLYMPUS_TUMBLE
using FortuneForge.Games.GodsOfOlympus;
#endif

internal sealed class ClassicPaidSlotOutcomes(IGameRandomSource random)
{
    public PaidSlotOutcomeResponse Resolve(PaidSlotOutcomeRequest request)
    {
#if ARCANE_ARCHIVES
        return new ArcanePaidSlotOutcomes(random).Resolve(request);
#else
        PaidSlotOutcomeProtocol.ValidateRequest(request, SelectedEngine.GameId);
        if (!request.IsFreeSpin && request.FreeSpinFeatureMode is not null)
            throw new ArgumentException("The admission uses an unsupported feature.", nameof(request));

        var engine = new SelectedEngine(random);
#if OLYMPUS_TUMBLE
        if (!request.IsFreeSpin)
        {
            if (request.MathModelId is not { } mathModel || !OlympusTumbleRules.SupportsPaidEngineVersion(mathModel) ||
                request.SpecialBoostApplied || request.FreeSpinsRemaining != 0)
                throw new ArgumentException("A base Olympus tumble admission is required.", nameof(request));

            return PaidSlotOutcomeResponse.Create(request, PaidSlotOutcomeProtocol.OlympusTumbleKind,
                engine.SpinTumble(request.WagerPoints, showcase: false, engineVersion: mathModel));
        }
#endif
        if (request.MathModelId is not null)
            throw new ArgumentException("The admission uses an unsupported math model.", nameof(request));

        return PaidSlotOutcomeResponse.Create(request, PaidSlotOutcomeProtocol.ClassicSlotKind,
            engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
                request.EnergyBalance, request.FreeSpinFeatureMode, request.PityMissesBefore, request.IsFreeSpin));
#endif
    }
}
