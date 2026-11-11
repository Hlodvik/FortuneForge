using FortuneForge.Games.Hosting;
using FortuneForge.Games.Slots.Runtime;

var random = new CryptographicSlotRandom();
#if OLYMPUS_TUMBLE
var app = GameServiceHost.Create(args, SelectedEngine.GameId, SelectedEngine.ClassicEngineVersion);
app.MapDemoAction("/internal/v1/demo/tumble-spins", (OlympusDemoSpinRequest request) =>
    new SelectedEngine(random).SpinTumble(request.WagerPoints, request.Showcase,
        request.EngineVersion ?? SelectedEngine.EngineVersion));
#else
var app = GameServiceHost.Create(args, SelectedEngine.GameId, SelectedEngine.EngineVersion);
#endif
app.MapDemoAction("/internal/v1/demo/spins", (ClassicDemoSpinRequest request) =>
    new SelectedEngine(random).Spin(request.WagerPoints, "public-demo", false, request.EnergyBalance,
        request.FeatureMode, request.PityMissesBefore, request.IsFreeSpin));
#if ARCANE_LIGHTNING
app.MapDemoAction("/internal/v1/demo/lightning-spins", (ClassicDemoSpinRequest request) =>
    new SelectedEngine(random).SpinDetailed(request.WagerPoints, "public-demo", false, request.EnergyBalance,
        request.FeatureMode, request.PityMissesBefore, request.IsFreeSpin));
#endif
app.Run();

sealed record ClassicDemoSpinRequest(long WagerPoints, int PityMissesBefore = 0,
    long EnergyBalance = 0, string? FeatureMode = null, bool IsFreeSpin = false);
#if OLYMPUS_TUMBLE
sealed record OlympusDemoSpinRequest(long WagerPoints, bool Showcase = false, string? EngineVersion = null);
#endif
