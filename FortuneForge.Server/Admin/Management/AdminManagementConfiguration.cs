using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Accounts.Security;
using System.Threading.RateLimiting;

namespace FortuneForge.Server.Admin.Management;

public static class AdminManagementConfiguration
{
    public const string WriteRateLimitPolicy = "admin-management-writes";
    public static IServiceCollection AddAdminManagement(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options => options.Filters.Add<ManagedPlayerSupplyExceptionFilter>());
        services.AddRateLimiter(options => options.AddPolicy(WriteRateLimitPolicy, context =>
            RateLimitPartition.GetFixedWindowLimiter(ClientRequestIdentity.GetSessionPartitionKey(context),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, QueueLimit = 0, Window = TimeSpan.FromMinutes(1), AutoReplenishment = true })));
        services.AddOptions<AdminManagementOptions>().Bind(configuration.GetSection("AdminManagement"))
            .PostConfigure(options => options.CursorSigningKey ??= configuration["AdminOperations:CursorSigningKey"])
            .Validate(options => !AdminManagementFeature.Enabled(configuration) || options.CursorSigningKey?.Length >= 32,
                "AdminManagement:CursorSigningKey must contain at least 32 characters when management is enabled.")
            .Validate(options => options.AllowedOrigins.Length <= 16 && options.AllowedOrigins.All(AdminManagementRequestSecurity.IsOrigin),
                "AdminManagement:AllowedOrigins must contain at most 16 explicit HTTP(S) origins.")
            .ValidateOnStart();
        services.AddSingleton<IAdminManagementStore, FirestoreAdminManagementStore>();
        services.AddSingleton<AdminManagementService>();
        // Normally provided by AddAdminOperations; TryAdd keeps isolated hosts safe.
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<IAdminOperationsAuthorizer, AccountAdminOperationsAuthorizer>(services);
        return services;
    }
}

public sealed class AdminManagementOptions
{
    public string? CursorSigningKey { get; set; }
    public string[] AllowedOrigins { get; set; } = ["https://fortuneforgegame.web.app", "https://fortuneforgegame.firebaseapp.com"];
}

internal static class AdminManagementFeature
{
    public static bool Enabled(IConfiguration configuration) => configuration.GetValue("Features:AdminManagementEnabled", false);
}
