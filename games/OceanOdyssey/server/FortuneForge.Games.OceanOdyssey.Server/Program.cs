using FortuneForge.Games.Hosting;
using FortuneForge.Games.OceanOdyssey;
using FortuneForge.Games.OceanOdyssey.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, OceanOdysseyEngine.GameId, OceanOdysseyEngine.EngineVersion);
var outcomes = new OceanOdysseyPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(OceanOdysseyEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
