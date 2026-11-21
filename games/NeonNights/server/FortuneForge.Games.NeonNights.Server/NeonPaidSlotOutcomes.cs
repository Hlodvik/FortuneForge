using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

namespace FortuneForge.Games.NeonNights.Server;

public sealed class NeonPaidSlotOutcomes(IGameRandomSource random)
{
    private readonly NeonNightsEngine engine = new(random);

    public PaidSlotOutcomeResponse Resolve(PaidSlotOutcomeRequest request)
    {
        var options = PaidNeonSpinOptions.Read(request);
        if (request.MathModelId is not null || request.FreeSpinFeatureMode is { } featureMode &&
            (!request.IsFreeSpin || !NeonPlayPolicy.IsFeatureMode(featureMode)))
            throw new ArgumentException("The admitted Neon Nights feature or math model is invalid.");

        var outcome = engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
            request.EnergyBalance, request.FreeSpinFeatureMode, request.PityMissesBefore,
            request.IsFreeSpin, options.IsDiscoPlay, options.DiscoMultiplier ?? NeonDiscoCascade.InitialMultiplier);
        return PaidSlotOutcomeResponse.Create(request, "neon-nights", outcome);
    }
}
