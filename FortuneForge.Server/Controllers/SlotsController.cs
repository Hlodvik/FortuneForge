using FortuneForge.Server.Slots.Spins;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Server.Controllers;

public sealed record SpinRequest(string GameId, long WagerPoints);

[ApiController]
[Route("api/slots")]
public sealed class SlotsController(SpinService spinService) : ControllerBase
{
    [HttpPost("spins")]
    public ActionResult Spin(SpinRequest request)
    {
        try
        {
            return Ok(spinService.Spin(request.GameId, request.WagerPoints));
        }
        catch (KeyNotFoundException exception)
        {
            return NotFound(new { error = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return Problem(exception.Message, statusCode: StatusCodes.Status500InternalServerError);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}
