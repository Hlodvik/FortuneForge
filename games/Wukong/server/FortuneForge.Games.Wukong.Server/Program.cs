using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;
using FortuneForge.Games.Wukong;
using FortuneForge.Games.Wukong.Server;

var app = GameServiceHost.Create(args, WukongEngine.GameId, WukongEngine.EngineVersion);
var outcomes = new WukongPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(WukongEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
