using FortuneForge.Games.Hosting;
using FortuneForge.Games.SamuraiFortune;
using FortuneForge.Games.SamuraiFortune.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, SamuraiFortuneEngine.GameId, SamuraiFortuneEngine.EngineVersion);
var outcomes = new SamuraiFortunePaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(SamuraiFortuneEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
