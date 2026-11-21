using FortuneForge.Games.Hosting;
using FortuneForge.Games.NeonNights;
using FortuneForge.Games.NeonNights.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, NeonArcadeSlotDefinitions.GameId, NeonArcadeSlotDefinitions.EngineVersion);
var outcomes = new NeonPaidSlotOutcomes(new CryptographicSlotRandom());

app.UseGameWorkerAuthentication(NeonArcadeSlotDefinitions.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);

app.Run();
