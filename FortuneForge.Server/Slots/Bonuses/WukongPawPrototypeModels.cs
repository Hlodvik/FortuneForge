namespace FortuneForge.Server.Slots.Bonuses;

// Local synthetic fixture input only. This is never a paid slot admission.
public sealed record WukongPawPrototypeAdmission(string SessionId, long WagerPoints,
    int? ForcedPawColumn, bool InjectEarnSymbol = false, string? AdmissionId = null, string? GrantId = null);

public sealed record WukongPawPrototypeGrant(string Id, long WagerPoints);
public sealed record WukongPawPrototypePlacement(string Id, string GrantId, int Column, long WagerPoints);
public sealed record WukongPawPrototypeResult(Guid SpinId, int PawCount, long MoneyGrabPoints, long TotalPoints);
public sealed record WukongPawPrototypeEarnRole(string SymbolId, int? Threshold = null,
    decimal? Rate = null, string? WagerBinding = null);

public sealed record WukongPawPrototypeState(string SessionId, string GameId, decimal PointValueInCents,
    long WagerPoints, IReadOnlyList<long> WagerOptions, IReadOnlyList<WukongPawPrototypeGrant> Grants,
    WukongPawPrototypePlacement? Placement, string Phase, IReadOnlyList<IReadOnlyList<string>> Reels,
    WukongPawPrototypeResult? LastResult, bool FixtureEarnSymbolInjection, int ObservedEarnScrolls,
    WukongPawPrototypeEarnRole EarnRole, string? Error = null)
{
    public string Mode => "synthetic-local";
    public bool LiveEarningEnabled => false;
    public bool LiveConsumptionEnabled => false;
}

public sealed record WukongPawPrototypePlaceRequest(string GrantId, int Column, long WagerPoints, string RequestId);
public sealed record WukongPawPrototypeClearRequest(string PlacementId, string RequestId);
public sealed record WukongPawPrototypeSpinRequest(string? PlacementId, long WagerPoints, string RequestId);
public sealed record WukongPawPrototypeFixtureRequest(string RequestId);
public sealed record WukongPawPrototypeEarnFixtureRequest(bool Enabled, string RequestId);

public sealed class WukongPawPrototypeException(string code, string message, int statusCode = 409)
    : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}
