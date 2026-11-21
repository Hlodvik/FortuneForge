using FortuneForge.Games.Hosting;
using FortuneForge.Games.CandyCarnival;
using FortuneForge.Games.CandyCarnival.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, CandyCarnivalEngine.GameId, CandyCarnivalEngine.EngineVersion);
var outcomes = new CandyCarnivalPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(CandyCarnivalEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
