using FortuneForge.Games.Hosting;
using FortuneForge.Games.HighNoonFortune;
using FortuneForge.Games.HighNoonFortune.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, HighNoonFortuneEngine.GameId, HighNoonFortuneEngine.EngineVersion);
var outcomes = new HighNoonPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(HighNoonFortuneEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
