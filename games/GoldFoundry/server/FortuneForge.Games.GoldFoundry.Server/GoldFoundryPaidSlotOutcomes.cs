using FortuneForge.Games.Slots;

namespace FortuneForge.Games.GoldFoundry.Server;

public sealed class GoldFoundryPaidSlotOutcomes(IGameRandomSource random)
{
    public const string OutcomeKind = "gold-foundry";
    private readonly GoldFoundryEngine engine = new(random);

    public PaidSlotOutcomeResponse Resolve(PaidSlotOutcomeRequest request)
    {
        PaidSlotOutcomeProtocol.ValidateRequest(request, GoldFoundryEngine.GameId);
        if (request.MathModelId is not null)
            throw new ArgumentException("Gold Foundry does not support that math model or feature.");
        var outcome = engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
            request.EnergyBalance, request.FreeSpinFeatureMode, request.PityMissesBefore, request.IsFreeSpin);
        return PaidSlotOutcomeResponse.Create(request, OutcomeKind, outcome);
    }
}
