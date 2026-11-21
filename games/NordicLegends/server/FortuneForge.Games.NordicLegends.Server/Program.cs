using FortuneForge.Games.Hosting;
using FortuneForge.Games.NordicLegends;
using FortuneForge.Games.NordicLegends.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, NordicLegendsEngine.GameId, NordicLegendsEngine.EngineVersion);
var outcomes = new NordicLegendsPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(NordicLegendsEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
