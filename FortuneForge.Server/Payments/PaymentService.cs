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

    private static PaymentResult<PaymentCheckoutResponse> ToResponse(
        PaymentResult<StoredPaymentCheckout> result) =>
        result.Value is null
            ? PaymentResult<PaymentCheckoutResponse>.Failure(result.Error)
            : PaymentResult<PaymentCheckoutResponse>.Success(result.Value.ToResponse());

    private static PaymentResult<PaymentWithdrawalResponse> ToResponse(
        PaymentResult<StoredPaymentWithdrawal> result) =>
        result.Value is null
            ? PaymentResult<PaymentWithdrawalResponse>.Failure(result.Error)
            : PaymentResult<PaymentWithdrawalResponse>.Success(result.Value.ToResponse());

    private static string CreateInvoiceId(string userId, DateTime createdAtUtc)
    {
        var normalizedUserId = string.Concat(userId.Where(char.IsLetterOrDigit));
        if (normalizedUserId.Length == 0)
        {
            throw new InvalidOperationException("A payment invoice requires an alphanumeric user ID.");
        }

        return normalizedUserId + createdAtUtc.ToString("ddMMyyHHmmssfff", CultureInfo.InvariantCulture);
    }

    private static string CreateWithdrawalId(DateTime createdAtUtc) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"FF-WD-{createdAtUtc:yyyyMMddHHmmssfff}-{CreateReferenceToken(4)}");

    private static PaymentCustomerDetails? CreateCustomerDetails(
        string accountEmail,
        CreatePaymentCheckoutRequest request) =>
        CreateCustomerDetails(
            accountEmail,
            request.CustomerFirstName,
            request.CustomerLastName,
            request.CustomerEmail);

    private static PaymentCustomerDetails? CreateCustomerDetails(
        string accountEmail,
        string? customerFirstName,
        string? customerLastName,
        string? customerEmail)
    {
        var firstName = NormalizeCustomerName(customerFirstName);
        var lastName = NormalizeCustomerName(customerLastName);
        var normalizedCustomerEmail = NormalizeEmail(customerEmail);
        var signedInEmail = NormalizeEmail(accountEmail);

        if (firstName is null ||
            lastName is null ||
            normalizedCustomerEmail is null ||
            signedInEmail is null ||
            !string.Equals(normalizedCustomerEmail, signedInEmail, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var customerReference = CreateCustomerReference();
        return new PaymentCustomerDetails(
            firstName,
            lastName,
            normalizedCustomerEmail,
            customerReference,
            customerReference);
    }

    private static string? NormalizeCustomerName(string? value)
    {
        var normalized = string.Join(
            ' ',
            (value ?? string.Empty)
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length is > 0 and <= 80 ? normalized : null;
    }

    private static string? NormalizeEmail(string? value)
    {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Length is > 0 and <= 254 && EmailPattern().IsMatch(normalized)
            ? normalized
            : null;
    }

    private static string CreateCustomerReference() => CreateReferenceToken(8);

    private static string CreateReferenceToken(int length)
    {
        Span<char> reference = stackalloc char[length];
        for (var index = 0; index < reference.Length; index++)
        {
            reference[index] = ReferenceAlphabet[RandomNumberGenerator.GetInt32(ReferenceAlphabet.Length)];
        }

        return new string(reference);
    }

    private static WithdrawalBankDetails? CreateWithdrawalBankDetails(
        CreatePaymentWithdrawalRequest request)
    {
        var bank = CreateBankDetails(
            request.AccountHolder,
            request.BankName,
            request.AccountNumber,
            request.BranchCode,
            request.AccountType);
        return bank is null
            ? null
            : new WithdrawalBankDetails(
                bank.AccountHolder,
                bank.BankName,
                bank.AccountNumber,
                bank.BranchCode,
                bank.AccountType);
    }

    private static PaymentBankDetails? CreateBankDetails(
        string? accountHolderValue,
        string? bankNameValue,
        string? accountNumberValue,
        string? branchCodeValue,
        string? accountTypeValue)
    {
        var accountHolder = NormalizeFreeText(accountHolderValue, 120);
        var bankName = NormalizeFreeText(bankNameValue, 120);
        var accountNumber = NormalizeBankDigits(accountNumberValue, 5, 20);
        var branchCode = NormalizeBankDigits(branchCodeValue, 3, 10);
        var accountType = NormalizeFreeText(accountTypeValue, 40);
        if (accountHolder is null ||
            bankName is null ||
            accountNumber is null ||
            branchCode is null ||
            accountType is null)
        {
            return null;
        }

        return new PaymentBankDetails(
            accountHolder,
            bankName,
            accountNumber,
            branchCode,
            accountType);
    }

    private static string? NormalizeFreeText(string? value, int maxLength)
    {
        var normalized = string.Join(
            ' ',
            (value ?? string.Empty)
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length is > 0 && normalized.Length <= maxLength ? normalized : null;
    }

    private static string? NormalizeBankDigits(string? value, int minLength, int maxLength)
    {
        var normalized = string.Concat((value ?? string.Empty).Where(char.IsDigit));
        return normalized.Length >= minLength && normalized.Length <= maxLength ? normalized : null;
    }

    private const string ReferenceAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    [GeneratedRegex("^[A-Za-z0-9_-]{16,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyKeyPattern();

    [GeneratedRegex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex CheckoutIdPattern();

    [GeneratedRegex("^[A-Za-z0-9]{16,192}$", RegexOptions.CultureInvariant)]
    private static partial Regex InvoiceIdPattern();

    [GeneratedRegex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
