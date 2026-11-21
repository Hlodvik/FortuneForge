using FortuneForge.Games.GodsOfOlympus;
using FortuneForge.Games.GodsOfOlympus.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, GodsOfOlympusEngine.GameId, GodsOfOlympusEngine.EngineVersion);
var outcomes = new OlympusPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(GodsOfOlympusEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
