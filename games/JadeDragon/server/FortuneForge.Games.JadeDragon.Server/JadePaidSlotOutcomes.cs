using FortuneForge.Games.JadeDragon.Models;
using FortuneForge.Games.Slots;

namespace FortuneForge.Games.JadeDragon.Server;

public sealed record JadeLegacyOutcome(SpinResult Result, SlotSpinEngineAudit Audit);

public sealed class JadePaidSlotOutcomes
{
    private readonly JadeDragonCascadeEngine cascade = new();
    private readonly JadeDragonEngine legacy = new();

    public PaidSlotOutcomeResponse Resolve(PaidSlotOutcomeRequest request)
    {
        PaidSlotOutcomeProtocol.ValidateRequest(request, JadeDragonEngine.GameId);
        if (request.MathModelId == JadeDragonCascadeRules.Version &&
            !request.SpecialBoostApplied && request.FreeSpinFeatureMode is null)
            return PaidSlotOutcomeResponse.Create(request, JadeDragonCascadeRules.Version,
                cascade.Spin(request.WagerPoints, request.IsFreeSpin));
        if (request.MathModelId is null && request.IsFreeSpin)
        {
            var result = legacy.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
                request.PityMissesBefore, request.FreeSpinFeatureMode, request.IsFreeSpin, request.EnergyBalance);
            return PaidSlotOutcomeResponse.Create(request, JadeDragonEngine.Version,
                new JadeLegacyOutcome(result, result.EngineAudit ?? throw new InvalidOperationException("The legacy Jade outcome is missing its audit.")));
        }
        throw new ArgumentException("Jade Dragon requires a cascade admission or a legacy free spin.");
    }
}
