using FortuneForge.Games.Hosting;
using FortuneForge.Games.MidnightVault;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, MidnightVaultEngine.GameId, MidnightVaultEngine.Version);
var random = new CryptographicSlotRandom();

app.MapDemoAction("/internal/v1/demo/spins", (MidnightDemoSpinRequest request) =>
{
    var engine = new MidnightVaultEngine(random);
    return engine.Spin(request.WagerPoints, "public-demo", featureMode: request.FeatureMode,
        persistedPityMisses: request.PityMissesBefore, isFreeSpin: request.IsFreeSpin, mathModelId: request.MathModelId);
});

app.Run();

sealed record MidnightDemoSpinRequest(long WagerPoints, int PityMissesBefore, string? FeatureMode, bool IsFreeSpin, string? MathModelId);
