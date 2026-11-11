using FortuneForge.Games.Hosting;
using FortuneForge.Games.JadeDragon;

var app = GameServiceHost.Create(args, JadeDragonEngine.GameId, JadeDragonEngine.Version);
var engine = new JadeDragonEngine();

app.MapDemoAction("/internal/v1/demo/spins", (JadeDemoSpinRequest request) =>
    engine.Spin(request.WagerPoints, "public-demo", pityMisses: request.PityMissesBefore,
        freeSpinFeatureMode: request.FeatureMode, isFreeSpin: request.IsFreeSpin));

app.Run();

sealed record JadeDemoSpinRequest(long WagerPoints, int PityMissesBefore, string? FeatureMode, bool IsFreeSpin);
