using FortuneForge.Server.Matchmaking;
using Google.Cloud.Firestore;

namespace FortuneForge.Server.Games.Hearts;

public static class PaidHeartsConfiguration
{
    public static IServiceCollection AddPaidHearts(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Cards:PaidHearts").Get<PaidHeartsOptions>() ?? new();
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton(provider => new FirestorePaidHeartsStore(
            provider.GetRequiredService<FirestoreDb>(), provider.GetRequiredService<IMultiplayerMatchmaker>(), options,
            provider.GetRequiredService<ILogger<FirestorePaidHeartsStore>>()));
        services.AddHostedService<PaidHeartsWorker>();
        return services;
    }
}

internal sealed class PaidHeartsWorker(FirestorePaidHeartsStore store, IConfiguration configuration, ILogger<PaidHeartsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (!configuration.GetValue("Features:HeartsEnabled", false)) continue;
            try { await store.SweepAsync(DateTime.UtcNow, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Paid Hearts progression failed; it will retry on the next sweep."); }
        }
    }
}
