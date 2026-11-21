using FortuneForge.Games.Slots;

namespace FortuneForge.Games.RainbowRealm.Server;

public sealed class RainbowPaidSlotOutcomes(IGameRandomSource random)
{
    private readonly RainbowRealmEngine engine = new(random);

    public PaidSlotOutcomeResponse Resolve(PaidSlotOutcomeRequest request)
    {
        PaidSlotOutcomeProtocol.ValidateRequest(request, RainbowRealmEngine.GameId);
        if ((!request.IsFreeSpin && request.FreeSpinFeatureMode is not null) || request.MathModelId is not null)
            throw new ArgumentException("The admission uses unsupported Rainbow Realm rules.", nameof(request));

        return PaidSlotOutcomeResponse.Create(request, PaidSlotOutcomeProtocol.ClassicSlotKind,
            engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
                request.EnergyBalance, request.FreeSpinFeatureMode, request.PityMissesBefore, request.IsFreeSpin));
    }
}
