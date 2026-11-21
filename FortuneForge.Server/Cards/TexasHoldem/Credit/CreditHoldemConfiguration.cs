namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

public static class CreditHoldemConfiguration
{
    // The shared host authenticates and forwards; only the game worker runs the lifecycle and sweeper.
    public static IServiceCollection AddCreditHoldem(this IServiceCollection services, IConfiguration configuration) => services;
}
