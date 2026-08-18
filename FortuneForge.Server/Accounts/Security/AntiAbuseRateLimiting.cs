using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Accounts.Security;

public static class RateLimitPolicies
{
    public const string AccountAuthentication = "account-authentication";
    public const string AccountCreation = "account-creation";
    public const string EmailVerification = "email-verification";
    public const string PaymentReads = "payment-reads";
    public const string PaymentWebhooks = "payment-webhooks";
    public const string PaymentWrites = "payment-writes";
    public const string SlotReads = "slot-reads";
    public const string SlotSpins = "slot-spins";
    public const string AdminOperationsReads = "admin-operations-reads";
    public const string CreditHoldemReads = "credit-holdem-reads";
    public const string CreditHoldemWrites = "credit-holdem-writes";
    public const string BlackjackTableReads = "blackjack-table-reads";
    public const string BlackjackTableWrites = "blackjack-table-writes";
}

public static class AntiAbuseRateLimiting
{
    public static IServiceCollection AddAntiAbuseRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ClientRequestIdentity.GetIpPartitionKey(context),
                    static _ => new FixedWindowRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        PermitLimit = 600,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1)
                    }));

            options.AddPolicy(RateLimitPolicies.SlotSpins, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    ClientRequestIdentity.GetSessionPartitionKey(context),
                    static _ => new TokenBucketRateLimiterOptions
                    {
                        AutoReplenishment = true,
                        QueueLimit = 0,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        TokenLimit = 6,
                        TokensPerPeriod = 2
                    }));
            options.AddPolicy(RateLimitPolicies.SlotReads, context =>
                FixedWindow(context, useSession: true, permitLimit: 60, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.AccountAuthentication, context =>
                FixedWindow(context, useSession: false, permitLimit: 10, TimeSpan.FromMinutes(5)));
            options.AddPolicy(RateLimitPolicies.AccountCreation, context =>
                FixedWindow(context, useSession: false, permitLimit: 5, TimeSpan.FromHours(1)));
            options.AddPolicy(RateLimitPolicies.EmailVerification, context =>
                FixedWindow(context, useSession: false, permitLimit: 5, TimeSpan.FromMinutes(10)));
            options.AddPolicy(RateLimitPolicies.PaymentReads, context =>
                FixedWindow(context, useSession: true, permitLimit: 60, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.PaymentWrites, context =>
                FixedWindow(context, useSession: true, permitLimit: 15, TimeSpan.FromMinutes(10)));
            options.AddPolicy(RateLimitPolicies.PaymentWebhooks, context =>
                FixedWindow(context, useSession: false, permitLimit: 120, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.AdminOperationsReads, context =>
                FixedWindow(context, useSession: true, permitLimit: 120, TimeSpan.FromMinutes(5)));
            options.AddPolicy(RateLimitPolicies.CreditHoldemReads, context =>
                FixedWindow(context, useSession: true, permitLimit: 120, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.CreditHoldemWrites, context =>
                FixedWindow(context, useSession: true, permitLimit: 30, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.BlackjackTableReads, context =>
                FixedWindow(context, useSession: true, permitLimit: 120, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RateLimitPolicies.BlackjackTableWrites, context =>
                FixedWindow(context, useSession: true, permitLimit: 30, TimeSpan.FromMinutes(1)));

            options.OnRejected = async (rejectionContext, cancellationToken) =>
            {
                var retryAfter = rejectionContext.Lease.TryGetMetadata(
                    MetadataName.RetryAfter,
                    out var retryAfterValue)
                        ? retryAfterValue
                        : TimeSpan.FromSeconds(1);
                var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
                var response = rejectionContext.HttpContext.Response;
                response.Headers.RetryAfter = retryAfterSeconds.ToString();

                var logger = rejectionContext.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("FortuneForge.AntiAbuse");
                logger.LogWarning(
                    "Rate limit rejected {Method} {Path}; trace {TraceIdentifier}.",
                    rejectionContext.HttpContext.Request.Method,
                    rejectionContext.HttpContext.Request.Path,
                    rejectionContext.HttpContext.TraceIdentifier);

                await response.WriteAsJsonAsync(new
                {
                    error = "Too many requests. Wait a moment and try again.",
                    title = "Request rate limited",
                    detail = "Too many requests were sent in a short period.",
                    retryAfterMilliseconds = checked(retryAfterSeconds * 1_000)
                }, cancellationToken);
            };
        });

        return services;
    }

    private static RateLimitPartition<string> FixedWindow(
        HttpContext context,
        bool useSession,
        int permitLimit,
        TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            useSession
                ? ClientRequestIdentity.GetSessionPartitionKey(context)
                : ClientRequestIdentity.GetIpPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = window
            });
}
