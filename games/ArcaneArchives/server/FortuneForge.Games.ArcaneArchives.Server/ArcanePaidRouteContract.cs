namespace FortuneForge.Games.ArcaneArchives.Server;

public static class ArcanePaidRouteContract
{
    public const string SpinPath = "/api/slots/spins";
    public const string PreparedSpinPath = ArcaneFeatureEndpoints.RoutePrefix + ArcaneFeatureEndpoints.PreparedSpinRoute;
    public const string StatePath = ArcaneFeatureEndpoints.RoutePrefix + "/state";
    public const string ObsoleteRoundCode = "arcane-obsolete-round";
}

public sealed record ArcanePaidSpinCommand(string GameId, long WagerPoints, bool UseFreeSpin, bool UseSpecialBoost);
