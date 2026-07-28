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
}
