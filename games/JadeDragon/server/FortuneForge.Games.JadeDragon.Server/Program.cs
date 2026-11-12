using FortuneForge.Games.Hosting;
using FortuneForge.Games.JadeDragon;
using FortuneForge.Games.Slots;

var app = GameServiceHost.Create(args, JadeDragonEngine.GameId, JadeDragonCascadeRules.Version);
var cascade = new JadeDragonCascadeEngine();
var legacy = new JadeDragonEngine();

app.UseGameWorkerAuthentication(JadeDragonEngine.GameId);
app.MapGameAction(PaidSlotOutcomeProtocol.Route, (PaidSlotOutcomeRequest request) =>
{
    PaidSlotOutcomeProtocol.ValidateRequest(request, JadeDragonEngine.GameId);
    if (request.IsNeonDiscoPlay || request.NeonDiscoMultiplier is not null)
        throw new ArgumentException("Jade Dragon does not support Disco cascades.");
    if (request.MathModelId == JadeDragonCascadeRules.Version &&
        !request.SpecialBoostApplied && request.FreeSpinFeatureMode is null)
        return PaidSlotOutcomeResponse.Create(request, JadeDragonCascadeRules.Version,
            cascade.Spin(request.WagerPoints, request.IsFreeSpin));
    if (request.MathModelId is null && request.IsFreeSpin)
        return PaidSlotOutcomeResponse.Create(request, JadeDragonEngine.Version,
            legacy.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
                request.PityMissesBefore, request.FreeSpinFeatureMode, request.IsFreeSpin, request.EnergyBalance));
    throw new ArgumentException("Jade Dragon requires a cascade admission or a legacy free spin.");
}, request => request.PlayerId);

app.Run();
