using Microsoft.Extensions.Options;

namespace FortuneForge.Server.SecurityAudit;

internal sealed class SecurityAuditInvestigationWorker(SecurityAuditInvestigationService service,
    IOptions<SecurityAuditInvestigationOptions> options, ILogger<SecurityAuditInvestigationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try { processed = await service.ProcessAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Security investigation processing will retry; pending markers and evidence holds are retained.");
            }
            try { await Task.Delay(processed > 0 ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(options.Value.PollSeconds), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

public static class SecurityAuditInvestigationConfiguration
{
    public static IServiceCollection AddSecurityAuditInvestigations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SecurityAuditInvestigationOptions>().Bind(configuration.GetSection("SecurityAuditInvestigations"))
            .Validate(value => value.BatchSize is >= 1 and <= 100 && value.CapturePageSize is >= 1 and <= 100 &&
                value.PollSeconds is >= 10 and <= 3600 && value.PairReviewMinimumSharedRounds is >= 2 and <= 100000 &&
                value.PairReviewMinimumOpposedOutcomes >= 1 && value.PairReviewMinimumOpposedOutcomes <= value.PairReviewMinimumSharedRounds)
            .Validate(value => !value.EnableVerifiedNetworkPairPriority || VerifiedNetworkPriorityConfigured(configuration),
                "Pair network priority requires enabled request recording and a verified strict trusted-proxy ingress configuration.")
            .ValidateOnStart();
        services.AddSingleton<SecurityAuditInvestigationService>();
        services.AddHostedService<SecurityAuditInvestigationWorker>();
        services.AddSingleton<SecurityAuditRawRetention>();
        services.AddHostedService<SecurityAuditRawRetentionWorker>();
        return services;
    }

    internal static bool VerifiedNetworkPriorityConfigured(IConfiguration configuration)
    {
        var request = configuration.GetSection("GameSecurityRequests").Get<GameSecurityRequestOptions>() ?? new();
        return request.Enabled && request.IpMode == "TrustedProxyChain" && request.RequireTrustedProxy &&
            GameRequestIpObservation.ValidOptions(request);
    }
}
