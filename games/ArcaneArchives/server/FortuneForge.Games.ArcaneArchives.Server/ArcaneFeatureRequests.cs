namespace FortuneForge.Games.ArcaneArchives.Server;

public sealed record ArcanePotionActivationRequest(long PotionId);

public sealed record ArcaneTomeRequest(string Page, bool Activate);
