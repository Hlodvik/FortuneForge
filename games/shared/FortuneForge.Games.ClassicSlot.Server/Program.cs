using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, SelectedEngine.GameId, SelectedEngine.EngineVersion);
app.UseGameWorkerAuthentication(SelectedEngine.GameId);
var outcomes = new ClassicPaidSlotOutcomes(new CryptographicSlotRandom());
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(
    PaidSlotOutcomeProtocol.Route, outcomes.Resolve, accountSelector: request => request.PlayerId);
app.Run();
