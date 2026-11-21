namespace FortuneForge.Server.Cards.HouseHoldem;

/// <summary>Canonical backend identity and legacy collection contract.</summary>
public static class HouseHoldemBackend
{
    public const string GameId = "house-holdem";
    public const string ApiPath = "/api/games/house-holdem";
    public const string EnabledSetting = "Features:HouseHoldemEnabled";
    internal const string RoundsCollection = "houseHoldemRounds";
    internal const string EventsCollection = "houseHoldemRoundEvents";
    internal const string ActiveCollection = "houseHoldemActiveRounds";
    internal const string CommandsCollection = "houseHoldemPreparedCommands";
    internal const string Owner = "house-holdem-package-v1";
}
