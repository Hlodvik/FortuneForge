using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Payments;
using FortuneForge.Server.Payments.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Controllers;

public sealed partial class PaymentsController
{
    private async Task<AccountSummary?> AuthenticatedAccountAsync(
        CancellationToken cancellationToken)
    {
        var accountResult = await accountService.GetProfileAsync(
            AccountSessionCookie.Read(Request),
            cancellationToken);
        return accountResult.Value;
    }

    private Task<AccountAccessContext?> AuthenticatedAccessAsync(
        CancellationToken cancellationToken) =>
        accountService.GetAccessContextAsync(
            AccountSessionCookie.Read(Request),
            cancellationToken);

    private ObjectResult UnauthorizedProblem() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Authentication required",
        detail: "Sign in before using payment services.");

    private ObjectResult FromError(PaymentError error) => error switch
    {
        PaymentError.UnsupportedMarket => Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Unsupported payment market",
            detail: "Choose a market returned by the payment catalog."),
        PaymentError.UnsupportedCurrency => Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Unsupported payment currency",
            detail: "The selected currency is not available for this market."),
        PaymentError.UnsupportedPaymentMethod => Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Unsupported payment method",
            detail: "Choose a payment method returned by the payment catalog."),
        PaymentError.InvalidAmount => Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid payment amount",
            detail: "Enter a whole-number amount within the range returned by the payment catalog."),
        PaymentError.InvalidCustomerDetails => Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid customer details",
            detail: "Enter the first name, last name, and signed-in email address for the customer attached to this payment."),
        PaymentError.InvalidBankDetails => Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid bank details",
            detail: "Enter the account holder, bank name, account number, branch code, and account type attached to this payment."),
        PaymentError.InvalidIdempotencyKey => Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid checkout request",
            detail: "A valid Idempotency-Key header is required."),
        PaymentError.IdempotencyConflict => Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Checkout request conflict",
            detail: "That Idempotency-Key was already used for a different checkout."),
        PaymentError.InvoiceConflict => Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Invoice ID conflict",
            detail: "The invoice timestamp collided with another request. Submit the payment again."),
        PaymentError.InvalidMockStatus => Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid mock status",
            detail: "Mock invoices can be simulated as processing, completed, or failed."),
        PaymentError.InvalidWithdrawalDetails => Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid withdrawal details",
            detail: "Enter the account holder, bank name, account number, branch code, and account type for the payout."),
        PaymentError.InsufficientCredits => Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Insufficient Rand",
            detail: "The account does not have enough Rand available for that withdrawal amount."),
        PaymentError.InvalidStatusTransition => Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Invoice status conflict",
            detail: "That invoice can no longer move to the requested status."),
        PaymentError.MockSimulationUnavailable => Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Mock simulation unavailable"),
        PaymentError.ProviderAuthenticationFailed => Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Payment service configuration error",
            detail: "Rand funding is temporarily unavailable. Try again later."),
        PaymentError.ProviderRejected => Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: "Rand funding could not be created",
            detail: "Rand funding is temporarily unavailable. Try again later."),
        PaymentError.PaymentPathwayUnavailable => Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Rand funding temporarily unavailable",
            detail: "This payment option is not ready yet. Contact support before trying this checkout again."),
        PaymentError.ProviderUnavailable => Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Payment service unavailable",
            detail: "Rand funding is temporarily unavailable. Try again later."),
        PaymentError.CheckoutNotFound => Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Checkout not found"),
        PaymentError.AccountNotFound => Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Account not found"),
        PaymentError.AccountBalanceNotFound => Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Account balance unavailable",
            detail: "The invoice was not completed and no Rand was added."),
        _ => Problem(statusCode: StatusCodes.Status500InternalServerError)
    };
}
