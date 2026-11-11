using FortuneForge.Server.Slots.Configuration;
using FortuneForge.Server.Slots.Evaluation;
using FortuneForge.Server.Slots.Games;
using FortuneForge.Server.Slots.Models;
using FortuneForge.Server.Slots.Payouts;
using FortuneForge.Server.Slots.Reels;
using FortuneForge.Server.Slots.Spins;
using FortuneForge.Server.RoundAudit;

namespace FortuneForge.Server.Configuration;

public static class SlotServicesConfiguration
{
    public static IServiceCollection AddSlotServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<ISlotsDefinitionProvider, OptionsSlotsDefinitionProvider>();
        services.AddSingleton<IRandomIndexSource, CryptoRandomIndexSource>();
        services.AddSingleton<IReelGenerator, CryptoReelGenerator>();
        services.AddSingleton<ICombinationEvaluator, CombinationEvaluator>();
        services.AddSingleton<IPayoutCalculator, PayoutCalculator>();
        services.AddSingleton<StandaloneSlotPackageRegistry>();
        services.AddSingleton<SpinService>();
        services.AddSingleton<IDemoSlotAuditStore, FirestoreDemoSlotAuditStore>();

        return services;
    }
}
