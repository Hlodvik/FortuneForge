using FortuneForge.Games.Hosting;
using FortuneForge.Games.PiratesFortune;
using FortuneForge.Games.PiratesFortune.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, PiratesFortuneEngine.GameId, PiratesFortuneEngine.EngineVersion);
var outcomes = new PiratesPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(PiratesFortuneEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
