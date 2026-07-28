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
}
