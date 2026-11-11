namespace FortuneForge.Games.NeonNights;

public sealed record NeonDemoEngineRequest(
    long WagerPoints,
    int PityMissesBefore,
    bool IsFreeSpin,
    string? FeatureMode,
    bool IsDiscoPlay,
    int DiscoMultiplier);
