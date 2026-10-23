using FortuneForge.Server.Bots;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FortuneForge.Server.Admin.Management;

internal sealed class ManagedPlayerSupplyExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ManagedPlayerSupplyUnavailableException) return;
        context.Result = new ObjectResult(new { code = "managed-opponents-unavailable", message = context.Exception.Message, error = context.Exception.Message })
        { StatusCode = StatusCodes.Status503ServiceUnavailable };
        context.ExceptionHandled = true;
    }
}
