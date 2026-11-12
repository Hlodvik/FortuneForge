using FortuneForge.Games.Hosting;
using FortuneForge.Games.NeonNights;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, NeonArcadeSlotDefinitions.GameId, NeonArcadeSlotDefinitions.EngineVersion);
var engine = new NeonNightsEngine(new CryptographicSlotRandom());

app.UseGameWorkerAuthentication(NeonArcadeSlotDefinitions.GameId);
app.MapGameAction(PaidSlotOutcomeProtocol.Route, (PaidSlotOutcomeRequest request) =>
{
    PaidSlotOutcomeProtocol.ValidateRequest(request, NeonArcadeSlotDefinitions.GameId);
    if (request.MathModelId is not null || request.FreeSpinFeatureMode is { } featureMode &&
        (!request.IsFreeSpin || !NeonPlayPolicy.IsFeatureMode(featureMode)))
        throw new ArgumentException("The admitted Neon Nights feature or math model is invalid.");

    var outcome = engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
        request.EnergyBalance, request.FreeSpinFeatureMode, request.PityMissesBefore,
        request.IsFreeSpin, request.IsNeonDiscoPlay, request.NeonDiscoMultiplier ?? NeonDiscoCascade.InitialMultiplier);
    return PaidSlotOutcomeResponse.Create(request, "neon-nights", outcome);
}, request => request.PlayerId);

app.Run();
