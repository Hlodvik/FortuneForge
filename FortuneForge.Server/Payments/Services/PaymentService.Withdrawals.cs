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
    public async Task<PaymentResult<PaymentWithdrawalResponse>> CreateWithdrawalAsync(
        string userId,
        string accountEmail,
        string? idempotencyKey,
        CreatePaymentWithdrawalRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) ||
            !IdempotencyKeyPattern().IsMatch(idempotencyKey))
        {
            return PaymentResult<PaymentWithdrawalResponse>.Failure(
                PaymentError.InvalidIdempotencyKey);
        }

        var market = PaymentCatalog.Markets.FirstOrDefault(candidate =>
            string.Equals(candidate.Code, request.Market?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (market is null)
        {
            return PaymentResult<PaymentWithdrawalResponse>.Failure(PaymentError.UnsupportedMarket);
        }

        if (!string.Equals(market.Currency, request.Currency?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return PaymentResult<PaymentWithdrawalResponse>.Failure(PaymentError.UnsupportedCurrency);
        }

        if (request.Amount < market.MinimumAmount || request.Amount > market.MaximumAmount)
        {
            return PaymentResult<PaymentWithdrawalResponse>.Failure(PaymentError.InvalidAmount);
        }

        long amountMinor;
        long creditsDebited;
        try
        {
            amountMinor = checked(request.Amount * 100);
            creditsDebited = checked(request.Amount * market.CreditsPerCurrencyUnit);
        }
        catch (OverflowException)
        {
            return PaymentResult<PaymentWithdrawalResponse>.Failure(PaymentError.InvalidAmount);
        }

        var customer = CreateCustomerDetails(
            accountEmail,
            request.CustomerFirstName,
            request.CustomerLastName,
            request.CustomerEmail);
        var bank = CreateWithdrawalBankDetails(request);
        if (customer is null)
        {
            return PaymentResult<PaymentWithdrawalResponse>.Failure(PaymentError.InvalidCustomerDetails);
        }

        if (bank is null)
        {
            return PaymentResult<PaymentWithdrawalResponse>.Failure(PaymentError.InvalidWithdrawalDetails);
        }

        var createdAtUtc = DateTime.UtcNow;
        var result = await _provider.CreateWithdrawalAsync(
            new PaymentWithdrawalDraft(
                userId,
                CreateWithdrawalId(createdAtUtc),
                idempotencyKey,
                market,
                request.Amount,
                amountMinor,
                creditsDebited,
                customer,
                bank,
                createdAtUtc),
            cancellationToken);
        return ToResponse(result);
    }
}
