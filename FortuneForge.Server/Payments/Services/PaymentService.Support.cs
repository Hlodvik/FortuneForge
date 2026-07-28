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
