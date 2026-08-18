namespace FortuneForge.Server.Admin.Operations;

public static class AdminOperationsConfiguration
{
    public static IServiceCollection AddAdminOperations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AdminOperationsOptions>()
            .Bind(configuration.GetSection(AdminOperationsOptions.SectionName))
            .Validate(options =>
                !AdminOperationsFeature.IsEnabled(configuration) ||
                options.CursorSigningKey.Length >= 32,
                "AdminOperations:CursorSigningKey must be at least 32 characters when admin operations are enabled.")
            .Validate(options => options.MaximumRangeDays is >= 1 and <= 31,
                "AdminOperations:MaximumRangeDays must be from 1 through 31.")
            .Validate(options => options.MaximumDocumentsPerCollection is >= 100 and <= 25_000,
                "AdminOperations:MaximumDocumentsPerCollection must be from 100 through 25000.")
            .ValidateOnStart();
        services.AddSingleton<IAdminOperationsStore, FirestoreAdminOperationsStore>();
        services.AddSingleton<IAdminOperationsAuthorizer, AccountAdminOperationsAuthorizer>();
        services.AddSingleton<AdminOperationsService>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
