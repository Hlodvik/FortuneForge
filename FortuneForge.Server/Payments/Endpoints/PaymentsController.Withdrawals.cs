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
}
