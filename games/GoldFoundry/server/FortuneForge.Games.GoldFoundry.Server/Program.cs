using FortuneForge.Games.GoldFoundry;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, GoldFoundryEngine.GameId, GoldFoundryEngine.Version);
var engine = new GoldFoundryEngine(new CryptographicSlotRandom());

app.UseGameWorkerAuthentication(GoldFoundryEngine.GameId);
app.MapGameAction(PaidSlotOutcomeProtocol.Route, (PaidSlotOutcomeRequest request) =>
{
    PaidSlotOutcomeProtocol.ValidateRequest(request, GoldFoundryEngine.GameId);
    if (request.MathModelId is not null || request.IsNeonDiscoPlay || request.NeonDiscoMultiplier is not null)
        throw new ArgumentException("Gold Foundry does not support that math model or feature.");
    var outcome = engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
        request.EnergyBalance, request.FreeSpinFeatureMode, request.PityMissesBefore, request.IsFreeSpin);
    return PaidSlotOutcomeResponse.Create(request, "gold-foundry", outcome);
}, request => request.PlayerId);

app.Run();
