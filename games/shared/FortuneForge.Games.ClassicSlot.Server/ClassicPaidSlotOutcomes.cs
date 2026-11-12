using FortuneForge.Games.Slots;
#if OLYMPUS_TUMBLE
using FortuneForge.Games.GodsOfOlympus;
#endif

internal sealed class ClassicPaidSlotOutcomes(IGameRandomSource random)
{
    public PaidSlotOutcomeResponse Resolve(PaidSlotOutcomeRequest request)
    {
        PaidSlotOutcomeProtocol.ValidateRequest(request, SelectedEngine.GameId);
        if (request.IsNeonDiscoPlay || request.NeonDiscoMultiplier is not null ||
            !request.IsFreeSpin && request.FreeSpinFeatureMode is not null)
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
    }
}
