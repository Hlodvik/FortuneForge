using FortuneForge.Games.Hosting;
using FortuneForge.Games.MidnightVault;
using FortuneForge.Games.MidnightVault.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, MidnightVaultEngine.GameId, MidnightVaultEngine.Version);
var outcomes = new MidnightVaultPaidSlotOutcomes(new CryptographicSlotRandom());

app.UseGameWorkerAuthentication(MidnightVaultEngine.GameId);
app.MapGameAction<PaidSlotOutcomeRequest, PaidSlotOutcomeResponse>(PaidSlotOutcomeProtocol.Route,
    outcomes.Resolve, request => request.PlayerId);

app.Run();
