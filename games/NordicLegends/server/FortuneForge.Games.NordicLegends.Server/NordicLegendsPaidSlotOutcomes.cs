using FortuneForge.Games.Slots;

namespace FortuneForge.Games.NordicLegends.Server;

public sealed class NordicLegendsPaidSlotOutcomes(IGameRandomSource random)
{
    private readonly NordicLegendsEngine engine = new(random);

    public PaidSlotOutcomeResponse Resolve(PaidSlotOutcomeRequest request)
    {
        PaidSlotOutcomeProtocol.ValidateRequest(request, NordicLegendsEngine.GameId);
        if ((!request.IsFreeSpin && request.FreeSpinFeatureMode is not null) || request.MathModelId is not null)
            throw new ArgumentException("The admission uses unsupported Nordic Legends rules.", nameof(request));

        return PaidSlotOutcomeResponse.Create(request, PaidSlotOutcomeProtocol.ClassicSlotKind,
            engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
                request.EnergyBalance, request.FreeSpinFeatureMode, request.PityMissesBefore, request.IsFreeSpin));
    }
}
