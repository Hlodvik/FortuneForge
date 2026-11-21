using FortuneForge.Games.Hosting;
using FortuneForge.Games.PhantomManor;
using FortuneForge.Games.PhantomManor.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, PhantomManorEngine.GameId, PhantomManorEngine.EngineVersion);
var outcomes = new PhantomManorPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(PhantomManorEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
