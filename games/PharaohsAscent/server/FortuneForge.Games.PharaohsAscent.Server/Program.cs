using FortuneForge.Games.Hosting;
using FortuneForge.Games.PharaohsAscent;
using FortuneForge.Games.PharaohsAscent.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, PharaohsAscentEngine.GameId, PharaohsAscentEngine.EngineVersion);
var outcomes = new PharaohsAscentPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(PharaohsAscentEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
