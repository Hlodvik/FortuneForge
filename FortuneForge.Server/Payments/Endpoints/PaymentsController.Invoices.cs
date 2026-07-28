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
}
