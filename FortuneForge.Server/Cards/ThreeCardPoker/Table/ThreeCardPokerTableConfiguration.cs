namespace FortuneForge.Server.Cards.ThreeCardPoker.Table;

public static class ThreeCardPokerTableConfiguration
{
    // Public requests are forwarded to the package worker. The host owns no table worker or state.
    public static IServiceCollection AddThreeCardPokerTables(this IServiceCollection services) => services;
}
