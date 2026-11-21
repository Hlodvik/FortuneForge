using FortuneForge.Games.Slots;

namespace FortuneForge.Games.SamuraiFortune.Server;

public sealed class SamuraiFortunePaidSlotOutcomes(IGameRandomSource random)
{
    private readonly SamuraiFortuneEngine engine = new(random);

    public PaidSlotOutcomeResponse Resolve(PaidSlotOutcomeRequest request)
    {
        PaidSlotOutcomeProtocol.ValidateRequest(request, SamuraiFortuneEngine.GameId);
        if ((!request.IsFreeSpin && request.FreeSpinFeatureMode is not null) || request.MathModelId is not null)
            throw new ArgumentException("The admission uses unsupported Samurai Fortune rules.", nameof(request));

        return PaidSlotOutcomeResponse.Create(request, PaidSlotOutcomeProtocol.ClassicSlotKind,
            engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
                request.EnergyBalance, request.FreeSpinFeatureMode, request.PityMissesBefore, request.IsFreeSpin));
    }
}
