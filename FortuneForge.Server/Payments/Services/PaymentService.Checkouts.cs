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
    private readonly IPaymentProvider _provider;
    private readonly PaymentsOptions _options;

    internal PaymentService(
        IPaymentProvider provider,
        IOptions<PaymentsOptions> options)
    {
        _provider = provider;
        _options = options.Value;
    }

    public PaymentCatalogResponse GetCatalog() => new(
        _provider.Id,
        _provider.IsMock,
        _provider.IsMock && _options.MockSimulationEnabled,
        PaymentCatalog.Markets);

    public async Task<PaymentResult<PaymentCheckoutResponse>> CreateCheckoutAsync(
        string userId,
        string accountEmail,
        string? idempotencyKey,
        CreatePaymentCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            !IdempotencyKeyPattern().IsMatch(idempotencyKey))
        {
            return PaymentResult<PaymentCheckoutResponse>.Failure(
                PaymentError.InvalidIdempotencyKey);
        }

        var market = PaymentCatalog.Markets.FirstOrDefault(candidate =>
            string.Equals(candidate.Code, request.Market?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (market is null)
        {
            return PaymentResult<PaymentCheckoutResponse>.Failure(PaymentError.UnsupportedMarket);
        }

        if (!string.Equals(market.Currency, request.Currency?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return PaymentResult<PaymentCheckoutResponse>.Failure(PaymentError.UnsupportedCurrency);
        }

        var paymentMethod = market.PaymentMethods.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, request.PaymentMethodId?.Trim(), StringComparison.Ordinal));
        if (paymentMethod is null)
        {
            return PaymentResult<PaymentCheckoutResponse>.Failure(
                PaymentError.UnsupportedPaymentMethod);
        }

        if (request.Amount < market.MinimumAmount || request.Amount > market.MaximumAmount)
        {
            return PaymentResult<PaymentCheckoutResponse>.Failure(PaymentError.InvalidAmount);
        }

        long amountMinor;
        long credits;
        try
        {
            amountMinor = checked(request.Amount * 100);
            credits = checked(request.Amount * market.CreditsPerCurrencyUnit);
        }
        catch (OverflowException)
        {
            return PaymentResult<PaymentCheckoutResponse>.Failure(PaymentError.InvalidAmount);
        }

        var customer = CreateCustomerDetails(accountEmail, request);
        if (customer is null)
        {
            return PaymentResult<PaymentCheckoutResponse>.Failure(PaymentError.InvalidCustomerDetails);
        }

        var payerBank = CreateBankDetails(
            request.AccountHolder,
            request.BankName,
            request.AccountNumber,
            request.BranchCode,
            request.AccountType);
        if (payerBank is null)
        {
            return PaymentResult<PaymentCheckoutResponse>.Failure(PaymentError.InvalidBankDetails);
        }

        var createdAtUtc = DateTime.UtcNow;
        var result = await _provider.CreateCheckoutAsync(
            new PaymentCheckoutDraft(
                userId,
                CreateInvoiceId(userId, createdAtUtc),
                idempotencyKey,
                market,
                paymentMethod,
                request.Amount,
                amountMinor,
                credits,
                customer,
                payerBank,
                createdAtUtc,
                createdAtUtc.AddMinutes(_options.CheckoutLifetimeMinutes)),
            cancellationToken);
        return ToResponse(result);
    }

}
