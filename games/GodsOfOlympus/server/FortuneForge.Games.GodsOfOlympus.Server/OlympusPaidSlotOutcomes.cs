using FortuneForge.Games.Slots;

namespace FortuneForge.Games.GodsOfOlympus.Server;

public sealed class OlympusPaidSlotOutcomes(IGameRandomSource random)
{
    private readonly GodsOfOlympusEngine engine = new(random);

    public PaidSlotOutcomeResponse Resolve(PaidSlotOutcomeRequest request)
    {
        PaidSlotOutcomeProtocol.ValidateRequest(request, GodsOfOlympusEngine.GameId);
        if (!request.IsFreeSpin)
        {
            if (request.FreeSpinFeatureMode is not null || request.MathModelId is not { } version ||
                !OlympusTumbleRules.SupportsPaidEngineVersion(version) || request.SpecialBoostApplied || request.FreeSpinsRemaining != 0)
                throw new ArgumentException("A base Olympus tumble admission is required.", nameof(request));

            return PaidSlotOutcomeResponse.Create(request, PaidSlotOutcomeProtocol.OlympusTumbleKind,
                engine.SpinTumble(request.WagerPoints, showcase: false, engineVersion: version));
        }

        if (request.MathModelId is not null)
            throw new ArgumentException("The admission uses an unsupported Olympus classic model.", nameof(request));

        return PaidSlotOutcomeResponse.Create(request, PaidSlotOutcomeProtocol.ClassicSlotKind,
            engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
                request.EnergyBalance, request.FreeSpinFeatureMode, request.PityMissesBefore, request.IsFreeSpin));
    }
}
