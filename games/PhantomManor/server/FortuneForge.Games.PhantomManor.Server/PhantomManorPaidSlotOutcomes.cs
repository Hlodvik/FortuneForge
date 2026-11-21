using FortuneForge.Games.Slots;

namespace FortuneForge.Games.PhantomManor.Server;

public sealed class PhantomManorPaidSlotOutcomes(IGameRandomSource random)
{
    private readonly PhantomManorEngine engine = new(random);

    public PaidSlotOutcomeResponse Resolve(PaidSlotOutcomeRequest request)
    {
        PaidSlotOutcomeProtocol.ValidateRequest(request, PhantomManorEngine.GameId);
        if ((!request.IsFreeSpin && request.FreeSpinFeatureMode is not null) || request.MathModelId is not null)
            throw new ArgumentException("The admission uses unsupported Phantom Manor rules.", nameof(request));

        return PaidSlotOutcomeResponse.Create(request, PaidSlotOutcomeProtocol.ClassicSlotKind,
            engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
                request.EnergyBalance, request.FreeSpinFeatureMode, request.PityMissesBefore, request.IsFreeSpin));
    }
}
