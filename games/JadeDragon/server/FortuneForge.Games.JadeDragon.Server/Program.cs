using FortuneForge.Games.Hosting;
using FortuneForge.Games.JadeDragon;
using FortuneForge.Games.JadeDragon.Server;
using FortuneForge.Games.Slots;

var app = GameServiceHost.Create(args, JadeDragonEngine.GameId, JadeDragonCascadeRules.Version);
var outcomes = new JadePaidSlotOutcomes();

app.UseGameWorkerAuthentication(JadeDragonEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);

app.Run();
