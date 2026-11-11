using FortuneForge.Games.Hosting;
using FortuneForge.Games.NeonNights;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, NeonArcadeSlotDefinitions.GameId, NeonArcadeSlotDefinitions.EngineVersion);
var random = new CryptographicSlotRandom();

app.MapDemoAction("/internal/v1/demo/spins", (NeonDemoEngineRequest request) =>
{
    if (request.FeatureMode is not null &&
        (!request.IsFreeSpin || !NeonPlayPolicy.IsFeatureMode(request.FeatureMode)))
    {
        throw new ArgumentException("The feature mode is invalid for this demo spin.");
    }

    var engine = new NeonNightsEngine(random);
    return engine.Spin(request.WagerPoints, "public-demo", specialBoostApplied: false, currentEnergyBalance: 0,
        request.FeatureMode, request.PityMissesBefore, request.IsFreeSpin, request.IsDiscoPlay, request.DiscoMultiplier);
});

app.Run();
