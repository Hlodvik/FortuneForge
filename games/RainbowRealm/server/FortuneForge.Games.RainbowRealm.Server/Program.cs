using FortuneForge.Games.Hosting;
using FortuneForge.Games.RainbowRealm;
using FortuneForge.Games.RainbowRealm.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, RainbowRealmEngine.GameId, RainbowRealmEngine.EngineVersion);
var outcomes = new RainbowPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(RainbowRealmEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
