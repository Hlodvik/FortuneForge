using FortuneForge.Games.Slots;

namespace FortuneForge.Games.DesertTreasures.Server;

public sealed class DesertTreasuresPaidSlotOutcomes(IGameRandomSource random)
{
    private readonly DesertTreasuresEngine engine = new(random);

    public PaidSlotOutcomeResponse Resolve(PaidSlotOutcomeRequest request)
    {
        PaidSlotOutcomeProtocol.ValidateRequest(request, DesertTreasuresEngine.GameId);
        if ((!request.IsFreeSpin && request.FreeSpinFeatureMode is not null) || request.MathModelId is not null)
            throw new ArgumentException("The admission uses unsupported Desert Treasures rules.", nameof(request));

        return PaidSlotOutcomeResponse.Create(request, PaidSlotOutcomeProtocol.ClassicSlotKind,
            engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
                request.EnergyBalance, request.FreeSpinFeatureMode, request.PityMissesBefore, request.IsFreeSpin));
    }
}
