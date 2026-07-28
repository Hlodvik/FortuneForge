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
public sealed partial class PaymentsController(
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

}
