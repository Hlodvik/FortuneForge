using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Server.Payments.Configuration;
using FortuneForge.Server.Payments.Models;
using FortuneForge.Server.Payments.Storage;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Payments.Providers;

internal sealed record InvoiceReconciliationResult(
    StoredPaymentCheckout Checkout,
    PaymentReconciliationStatus Status);

internal sealed record MerchantGatewayInvoiceCreateRequest(
    string TheirNumber,
    decimal Amount,
    string Currency,
    string? PathwayKey,
    string? CustomerReference,
    string? BeneficiaryReference);

internal sealed record MerchantGatewayWithdrawalCreateRequest(
    string TheirNumber,
    decimal Amount,
    string Currency,
    string? PathwayKey,
    string AccountHolder,
    string BankName,
    string AccountNumber,
    string? BranchCode,
    string? AccountType);

internal sealed record MerchantGatewayCreatedResponse(
    Guid Id,
    long OurNumber,
    string Status,
    decimal? Amount,
    string? Currency,
    decimal? FeeAmount,
    decimal? NetAmount,
    string? RowVersion,
    bool? IdempotentReplay);

internal sealed record MerchantGatewayInvoiceResponse(
    Guid Id,
    long OurNumber,
    string TheirNumber,
    string? CustomerReference,
    string? BeneficiaryReference,
    decimal Amount,
    string Currency,
    decimal? FeeRate,
    decimal FeeAmount,
    decimal NetAmount,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string RowVersion);

internal sealed record MerchantGatewayPathwayResponse(
    string Key,
    string Name,
    string Bank,
    string? AccountHolder,
    string? AccountNumber,
    string? BranchCode,
    string? AccountType,
    decimal InvoiceRate,
    decimal WithdrawalRate);
