using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Server.Games.Hearts;

internal static class HeartsHostFallback
{
    internal static async Task<ActionResult> ExecuteAsync(ControllerBase controller, AccountService accounts,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (controller.ControllerContext.HttpContext is { } context)
            context.Response.Headers.CacheControl = "no-store, max-age=0";
        if (!configuration.GetValue(HeartsBackend.FreeAdmissionSetting, false))
            return controller.StatusCode(503, new HeartsErrorResponse("hearts-disabled", "Hearts is not available yet."));
        var account = (await accounts.GetProfileAsync(AccountSessionCookie.Read(controller.Request), cancellationToken)).Value;
        if (account is null)
            return controller.Unauthorized(new HeartsErrorResponse("hearts-authentication-required", "Sign in to play Hearts."));
        return controller.StatusCode(503, new HeartsErrorResponse("hearts-unavailable", "Hearts is temporarily unavailable. Retry your original request."));
    }
}
