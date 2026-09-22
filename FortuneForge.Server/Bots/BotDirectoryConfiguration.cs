using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Bots;

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
        return services;
    }
}
