using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Configuration;

public static class SlotServicesConfiguration
{
    public static IServiceCollection AddSlotServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SlotsOptions>()
            .Bind(configuration.GetSection(SlotsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SlotsOptions>, SlotsOptionsValidator>();
        services.AddSingleton<ISlotsDefinitionProvider, OptionsSlotsDefinitionProvider>();
        services.AddSingleton<IRandomIndexSource, CryptoRandomIndexSource>();
        services.AddSingleton<IReelGenerator, CryptoReelGenerator>();
        services.AddSingleton<ICombinationEvaluator, CombinationEvaluator>();
        services.AddSingleton<IPayoutCalculator, PayoutCalculator>();
        services.AddSingleton<SpinService>();

        return services;
    }
}
