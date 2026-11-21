using FortuneForge.Games.GoldFoundry;
using FortuneForge.Games.GoldFoundry.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, GoldFoundryEngine.GameId, GoldFoundryEngine.Version);
var outcomes = new GoldFoundryPaidSlotOutcomes(new CryptographicSlotRandom());

app.UseGameWorkerAuthentication(GoldFoundryEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);

app.Run();
