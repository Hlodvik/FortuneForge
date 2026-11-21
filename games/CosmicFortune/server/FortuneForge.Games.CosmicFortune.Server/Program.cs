using FortuneForge.Games.Hosting;
using FortuneForge.Games.CosmicFortune;
using FortuneForge.Games.CosmicFortune.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, CosmicFortuneEngine.GameId, CosmicFortuneEngine.EngineVersion);
var outcomes = new CosmicFortunePaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(CosmicFortuneEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
