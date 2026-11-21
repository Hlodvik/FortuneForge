using FortuneForge.Games.Hosting;
using FortuneForge.Games.DesertTreasures;
using FortuneForge.Games.DesertTreasures.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, DesertTreasuresEngine.GameId, DesertTreasuresEngine.EngineVersion);
var outcomes = new DesertTreasuresPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(DesertTreasuresEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
