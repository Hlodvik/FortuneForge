using FortuneForge.Games.Slots;

namespace FortuneForge.Games.MidnightVault.Server;

public sealed class MidnightVaultPaidSlotOutcomes(IGameRandomSource random)
{
    public const string OutcomeKind = "midnight-vault";
    private readonly MidnightVaultEngine engine = new(random);

    public PaidSlotOutcomeResponse Resolve(PaidSlotOutcomeRequest request)
    {
        PaidSlotOutcomeProtocol.ValidateRequest(request, MidnightVaultEngine.GameId);
        var outcome = engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied, request.EnergyBalance,
            request.FreeSpinFeatureMode, request.PityMissesBefore, request.IsFreeSpin, request.MathModelId);
        return PaidSlotOutcomeResponse.Create(request, OutcomeKind, outcome);
    }
}
