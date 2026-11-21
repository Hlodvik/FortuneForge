namespace FortuneForge.Server.Cards.Blackjack.Table;

public static class BlackjackTableConfiguration
{
    // Public requests are forwarded to the package worker. The host owns no table worker or state.
    public static IServiceCollection AddBlackjackTables(this IServiceCollection services) => services;
}
