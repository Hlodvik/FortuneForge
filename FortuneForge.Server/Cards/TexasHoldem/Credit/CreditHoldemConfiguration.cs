using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

public static class CreditHoldemConfiguration
{
    public static IServiceCollection AddCreditHoldem(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CreditHoldemOptions>(
            configuration.GetSection(CreditHoldemOptions.SectionName));
        services.AddSingleton<ICreditHoldemStore>(provider =>
            new FirestoreCreditHoldemStore(
                provider.GetRequiredService<FirestoreDb>(),
                provider.GetRequiredService<IOptions<CreditHoldemOptions>>().Value.AllowSingleHumanBotFill));
        services.AddSingleton<CreditHoldemService>();
        return services;
    }
}

public sealed class CreditHoldemOptions
{
    public const string SectionName = "Cards:CreditTexasHoldem";
    public bool AllowSingleHumanBotFill { get; set; }
}
