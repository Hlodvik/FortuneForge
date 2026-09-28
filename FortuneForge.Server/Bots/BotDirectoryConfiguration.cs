using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Bots;

using Google.Cloud.Firestore;

public static class BotDirectoryConfiguration
{
    public static IServiceCollection AddBotDirectory(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<BotDirectoryOptions>()
            .Bind(configuration.GetSection(BotDirectoryOptions.SectionName))
            .Validate(
                options => !BotDirectoryValidation.GetErrors(options).Any(),
                "The bot directory configuration is invalid.")
            .ValidateOnStart();
        services.AddSingleton<IBotDirectory>(provider => new ConfiguredBotDirectory(
            provider.GetRequiredService<IOptions<BotDirectoryOptions>>().Value));
        services.AddSingleton<IManagedPlayerProfileRepository>(provider =>
            new FirestoreManagedPlayerProfileRepository(
                provider.GetRequiredService<FirestoreDb>()));
        services.AddSingleton<IManagedPlayerAssignmentStore>(provider =>
            new FirestoreManagedPlayerAssignmentStore(
                provider.GetRequiredService<FirestoreDb>()));
        services.AddSingleton<IManagedPlayerProfileGenerator, ManagedPlayerProfileGenerator>();
        services.AddSingleton<IManagedPlayerQueuer, ManagedPlayerQueuer>();
        return services;
    }
}
