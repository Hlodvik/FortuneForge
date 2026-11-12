using FortuneForge.Games.Hosting;
using FortuneForge.Games.MidnightVault;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, MidnightVaultEngine.GameId, MidnightVaultEngine.Version);
var engine = new MidnightVaultEngine(new CryptographicSlotRandom());

app.UseGameWorkerAuthentication(MidnightVaultEngine.GameId);
app.MapGameAction(PaidSlotOutcomeProtocol.Route, (PaidSlotOutcomeRequest request) =>
{
    PaidSlotOutcomeProtocol.ValidateRequest(request, MidnightVaultEngine.GameId);
    if (request.IsNeonDiscoPlay || request.NeonDiscoMultiplier is not null)
        throw new ArgumentException("Midnight Vault does not support Disco cascades.");
    var outcome = engine.Spin(request.WagerPoints, request.PlayerId, request.SpecialBoostApplied,
        request.EnergyBalance, request.FreeSpinFeatureMode, request.PityMissesBefore,
        request.IsFreeSpin, request.MathModelId);
    return PaidSlotOutcomeResponse.Create(request, "midnight-vault", outcome);
}, request => request.PlayerId);

app.Run();
