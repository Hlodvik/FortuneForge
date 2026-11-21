using FortuneForge.Games.Hosting;
using FortuneForge.Games.RoyalDraw;
using FortuneForge.Games.RoyalDraw.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, RoyalDrawEngine.GameId, RoyalDrawEngine.EngineVersion);
var outcomes = new RoyalDrawPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(RoyalDrawEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
