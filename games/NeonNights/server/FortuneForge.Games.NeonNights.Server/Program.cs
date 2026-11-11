using System.Security.Cryptography;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.NeonNights;
using FortuneForge.Games.Slots;

var app = GameServiceHost.Create(args, NeonArcadeSlotDefinitions.GameId, NeonArcadeSlotDefinitions.EngineVersion);
var random = new CryptographicSlotRandom();

app.MapPost("/internal/v1/demo/spins", (NeonDemoEngineRequest request) =>
{
    try
    {
        if (request.FeatureMode is not null &&
            (!request.IsFreeSpin || !NeonPlayPolicy.IsFeatureMode(request.FeatureMode)))
        {
            throw new ArgumentException("The feature mode is invalid for this demo spin.");
        }

        var engine = new NeonNightsEngine(random);
        var outcome = engine.Spin(
            request.WagerPoints, "public-demo", specialBoostApplied: false, currentEnergyBalance: 0,
            request.FeatureMode, request.PityMissesBefore, request.IsFreeSpin,
            request.IsDiscoPlay, request.DiscoMultiplier);
        return Results.Ok(outcome);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.Run();

sealed class CryptographicSlotRandom : IGameRandomSource
{
    public int Next(int maximumExclusive) => RandomNumberGenerator.GetInt32(maximumExclusive);
}
