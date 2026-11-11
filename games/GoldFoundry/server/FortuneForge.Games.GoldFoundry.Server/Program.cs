using FortuneForge.Games.GoldFoundry;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots.Runtime;

var app = GameServiceHost.Create(args, GoldFoundryEngine.GameId, GoldFoundryEngine.Version);
var random = new CryptographicSlotRandom();

app.MapDemoAction("/internal/v1/demo/spins", (GoldDemoSpinRequest request) =>
{
    var engine = new GoldFoundryEngine(random);
    return engine.Spin(request.WagerPoints, "public-demo", currentEnergyBalance: request.EnergyBalance,
        persistedPityMisses: request.PityMissesBefore);
});

app.Run();

sealed record GoldDemoSpinRequest(long WagerPoints, int PityMissesBefore, long EnergyBalance);
