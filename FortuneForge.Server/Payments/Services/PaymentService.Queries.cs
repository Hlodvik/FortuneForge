using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Providers;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Payments;

public sealed partial class PaymentService
{
    public async Task<PaymentResult<PaymentCheckoutResponse>> GetCheckoutAsync(
        string userId,
        string checkoutId,
        CancellationToken cancellationToken)
    {
        if (!CheckoutIdPattern().IsMatch(checkoutId))
        {
            return PaymentResult<PaymentCheckoutResponse>.Failure(PaymentError.CheckoutNotFound);
        }

        var checkout = await _provider.GetCheckoutAsync(checkoutId, userId, cancellationToken);
        return checkout is null
            ? PaymentResult<PaymentCheckoutResponse>.Failure(PaymentError.CheckoutNotFound)
            : PaymentResult<PaymentCheckoutResponse>.Success(checkout.ToResponse());
    }

    public async Task<PaymentResult<PaymentCheckoutResponse>> GetInvoiceAsync(
        string userId,
        string invoiceId,
        CancellationToken cancellationToken)
    {
        if (!InvoiceIdPattern().IsMatch(invoiceId))
        {
            return PaymentResult<PaymentCheckoutResponse>.Failure(PaymentError.CheckoutNotFound);
        }

        var checkout = await _provider.GetInvoiceAsync(invoiceId, userId, cancellationToken);
        return checkout is null
            ? PaymentResult<PaymentCheckoutResponse>.Failure(PaymentError.CheckoutNotFound)
            : PaymentResult<PaymentCheckoutResponse>.Success(checkout.ToResponse());
    }

    public async Task<PaymentResult<PaymentCheckoutResponse>> GetInvoiceForAdminAsync(
        string invoiceId,
        CancellationToken cancellationToken)
    {
        if (!InvoiceIdPattern().IsMatch(invoiceId))
        {
            return PaymentResult<PaymentCheckoutResponse>.Failure(PaymentError.CheckoutNotFound);
        }

        var checkout = await _provider.GetInvoiceForAdminAsync(invoiceId, cancellationToken);
        return checkout is null
            ? PaymentResult<PaymentCheckoutResponse>.Failure(PaymentError.CheckoutNotFound)
            : PaymentResult<PaymentCheckoutResponse>.Success(checkout.ToResponse());
    }

    public async Task<PaymentInvoiceListResponse> ListInvoicesAsync(
        string userId,
        int limit,
        CancellationToken cancellationToken)
    {
        var invoices = await _provider.ListInvoicesAsync(
            userId,
            Math.Clamp(limit, 1, 50),
            cancellationToken);
        return new PaymentInvoiceListResponse(
            invoices.Select(invoice => invoice.ToResponse()).ToArray());
    }

    public async Task<PaymentResult<PaymentCheckoutResponse>> SimulateAsync(
        string userId,
        string checkoutId,
        string status,
        CancellationToken cancellationToken)
    {
        if (!_options.MockSimulationEnabled || _provider is not IMockPaymentSimulator simulator)
        {
            return PaymentResult<PaymentCheckoutResponse>.Failure(
                PaymentError.MockSimulationUnavailable);
        }

        if (!CheckoutIdPattern().IsMatch(checkoutId))
        {
            return PaymentResult<PaymentCheckoutResponse>.Failure(PaymentError.CheckoutNotFound);
        }

        return ToResponse(await simulator.SimulateAsync(
            checkoutId,
            userId,
            status,
            cancellationToken));
    }
}
