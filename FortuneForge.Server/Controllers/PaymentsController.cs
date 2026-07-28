using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Payments;
using FortuneForge.Server.Payments.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FortuneForge.Server.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController(
    PaymentService paymentService,
    PaymentWebhookService paymentWebhookService,
    AccountService accountService) : ControllerBase
{
    [HttpGet("catalog")]
    [EnableRateLimiting(RateLimitPolicies.PaymentReads)]
    [ProducesResponseType<PaymentCatalogResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Catalog(CancellationToken cancellationToken)
    {
        var account = await AuthenticatedAccountAsync(cancellationToken);
        return account is null
            ? UnauthorizedProblem()
            : Ok(paymentService.GetCatalog());
    }

    [HttpPost("checkouts")]
    [EnableRateLimiting(RateLimitPolicies.PaymentWrites)]
    [ProducesResponseType<PaymentCheckoutResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateCheckout(
        [FromBody] CreatePaymentCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
        {
            return UnauthorizedProblem();
        }

        var result = await paymentService.CreateCheckoutAsync(
            account.UserId,
            account.Email,
            Request.Headers["Idempotency-Key"].ToString(),
            request,
            cancellationToken);
        return result.Value is null
            ? FromError(result.Error)
            : Created($"/api/payments/invoices/{result.Value.InvoiceId}", result.Value);
    }

    [HttpGet("invoices")]
    [EnableRateLimiting(RateLimitPolicies.PaymentReads)]
    [ProducesResponseType<PaymentInvoiceListResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInvoices(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var account = await AuthenticatedAccountAsync(cancellationToken);
        return account is null
            ? UnauthorizedProblem()
            : Ok(await paymentService.ListInvoicesAsync(account.UserId, limit, cancellationToken));
    }

    [HttpPost("withdrawals")]
    [EnableRateLimiting(RateLimitPolicies.PaymentWrites)]
    [ProducesResponseType<PaymentWithdrawalResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateWithdrawal(
        [FromBody] CreatePaymentWithdrawalRequest request,
        CancellationToken cancellationToken)
    {
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
        {
            return UnauthorizedProblem();
        }

        var result = await paymentService.CreateWithdrawalAsync(
            account.UserId,
            account.Email,
            Request.Headers["Idempotency-Key"].ToString(),
            request,
            cancellationToken);
        return result.Value is null
            ? FromError(result.Error)
            : Created($"/api/payments/withdrawals/{result.Value.WithdrawalId}", result.Value);
    }

    [HttpGet("invoices/{invoiceId}")]
    [EnableRateLimiting(RateLimitPolicies.PaymentReads)]
    [ProducesResponseType<PaymentCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInvoice(
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
        {
            return UnauthorizedProblem();
        }

        var result = await paymentService.GetInvoiceAsync(
            account.UserId,
            invoiceId,
            cancellationToken);
        return result.Value is null ? FromError(result.Error) : Ok(result.Value);
    }

    [HttpGet("admin/users/{userId}/invoices")]
    [EnableRateLimiting(RateLimitPolicies.PaymentReads)]
    [ProducesResponseType<PaymentInvoiceListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListUserInvoicesForAdmin(
        string userId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var access = await AuthenticatedAccessAsync(cancellationToken);
        if (access is null)
        {
            return UnauthorizedProblem();
        }

        if (!access.IsAdmin)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid user ID",
                Detail = "Enter the immutable user ID attached to the account."
            });
        }

        return Ok(await paymentService.ListInvoicesAsync(userId, limit, cancellationToken));
    }

    [HttpGet("admin/invoices/{invoiceId}")]
    [EnableRateLimiting(RateLimitPolicies.PaymentReads)]
    [ProducesResponseType<PaymentCheckoutResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInvoiceForAdmin(
        string invoiceId,
        CancellationToken cancellationToken)
    {
        var access = await AuthenticatedAccessAsync(cancellationToken);
        if (access is null)
        {
            return UnauthorizedProblem();
        }

        if (!access.IsAdmin)
        {
            return Forbid();
        }

        var result = await paymentService.GetInvoiceForAdminAsync(invoiceId, cancellationToken);
        return result.Value is null ? FromError(result.Error) : Ok(result.Value);
    }

    [HttpGet("checkouts/{checkoutId}")]
    [EnableRateLimiting(RateLimitPolicies.PaymentReads)]
    [ProducesResponseType<PaymentCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCheckout(
        string checkoutId,
        CancellationToken cancellationToken)
    {
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
        {
            return UnauthorizedProblem();
        }

        var result = await paymentService.GetCheckoutAsync(
            account.UserId,
            checkoutId,
            cancellationToken);
        return result.Value is null ? FromError(result.Error) : Ok(result.Value);
    }

    [HttpPost("webhooks/merchantgateway")]
    [Consumes("application/json")]
    [EnableRateLimiting(RateLimitPolicies.PaymentWebhooks)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReceiveMerchantGatewayWebhook(
        CancellationToken cancellationToken)
    {
        if (Request.ContentLength is > 32 * 1024)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid payment event"
            });
        }

        await using var body = new MemoryStream();
        await Request.Body.CopyToAsync(body, cancellationToken);
        var result = await paymentWebhookService.HandleMerchantGatewayAsync(
            Request.Headers[PaymentWebhookService.EventIdHeader].ToString(),
            Request.Headers[PaymentWebhookService.EventTypeHeader].ToString(),
            Request.Headers[PaymentWebhookService.TimestampHeader].ToString(),
            Request.Headers[PaymentWebhookService.SignatureHeader].ToString(),
            body.ToArray(),
            cancellationToken);

        return result switch
        {
            PaymentWebhookStatus.Accepted or PaymentWebhookStatus.Duplicate => NoContent(),
            PaymentWebhookStatus.Invalid => BadRequest(new ProblemDetails
            {
                Title = "Invalid payment event"
            }),
            PaymentWebhookStatus.Unauthorized => Unauthorized(new ProblemDetails
            {
                Title = "Payment event authentication failed"
            }),
            PaymentWebhookStatus.Retryable => StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails
                {
                    Title = "Payment event processing is pending"
                }),
            PaymentWebhookStatus.Disabled => NotFound(),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
    }

    [HttpPost("mock/checkouts/{checkoutId}/simulate")]
    [EnableRateLimiting(RateLimitPolicies.PaymentWrites)]
    [ProducesResponseType<PaymentCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Simulate(
        string checkoutId,
        [FromBody] MockPaymentStatusRequest request,
        CancellationToken cancellationToken)
    {
        var account = await AuthenticatedAccountAsync(cancellationToken);
        if (account is null)
        {
            return UnauthorizedProblem();
        }

        var result = await paymentService.SimulateAsync(
            account.UserId,
            checkoutId,
            request.Status ?? string.Empty,
            cancellationToken);
        return result.Value is null ? FromError(result.Error) : Ok(result.Value);
    }

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
            title: "Insufficient credits",
            detail: "The account does not have enough slot credits available for that withdrawal amount."),
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
            detail: "Credit purchases are temporarily unavailable. Try again later."),
        PaymentError.ProviderRejected => Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: "Credit purchase could not be created",
            detail: "Credit purchases are temporarily unavailable. Try again later."),
        PaymentError.PaymentPathwayUnavailable => Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Credit purchase temporarily unavailable",
            detail: "This payment option is not ready yet. Contact support before trying this checkout again."),
        PaymentError.ProviderUnavailable => Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Payment service unavailable",
            detail: "Credit purchases are temporarily unavailable. Try again later."),
        PaymentError.CheckoutNotFound => Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Checkout not found"),
        PaymentError.AccountNotFound => Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Account not found"),
        PaymentError.AccountBalanceNotFound => Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Account balance unavailable",
            detail: "The invoice was not completed and no credits were added."),
        _ => Problem(statusCode: StatusCodes.Status500InternalServerError)
    };
}
