using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Providers;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Payments;

internal sealed class PaymentReconciliationWorker(
    IPaymentProvider provider,
    IOptions<PaymentsOptions> options,
    ILogger<PaymentReconciliationWorker> logger) : BackgroundService
{
    private readonly PaymentsOptions settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (provider is not IPaymentReconciler reconciler)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(
            settings.MerchantGateway.ReconciliationIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        await ReconcileAsync(reconciler, stoppingToken);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ReconcileAsync(reconciler, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
    }

    private async Task ReconcileAsync(
        IPaymentReconciler reconciler,
        CancellationToken cancellationToken)
    {
        try
        {
            var count = await reconciler.ReconcilePendingAsync(cancellationToken);
            if (count > 0)
            {
                logger.LogInformation(
                    "Reconciled {InvoiceCount} MerchantGateway invoices.",
                    count);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "MerchantGateway reconciliation failed; the next scheduled pass will retry.");
        }
    }
}
