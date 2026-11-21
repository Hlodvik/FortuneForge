using FortuneForge.Games.Hosting;
using FortuneForge.Games.RobotRevolution;
using FortuneForge.Games.RobotRevolution.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, RobotRevolutionEngine.GameId, RobotRevolutionEngine.EngineVersion);
var outcomes = new RobotRevolutionPaidSlotOutcomes(new CryptographicSlotRandom());
app.UseGameWorkerAuthentication(RobotRevolutionEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);
app.Run();
