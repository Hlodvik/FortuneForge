using FortuneForge.Games.Hosting;
using FortuneForge.Games.ReelRiches;
using FortuneForge.Games.ReelRiches.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, ReelRichesEngine.GameId, ReelRichesEngine.EngineVersion);
var outcomes = new ReelRichesPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(ReelRichesEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
