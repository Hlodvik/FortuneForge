namespace FortuneForge.Server.Accounts.Storage;

public sealed class AccountSchemaInitializer(
    IAccountStore accountStore,
    ILogger<AccountSchemaInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Ensuring account wallet and slot statistics schema.");
        await accountStore.InitializeSchemaAsync(cancellationToken);
        logger.LogInformation("Account wallet and slot statistics schema is ready.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
