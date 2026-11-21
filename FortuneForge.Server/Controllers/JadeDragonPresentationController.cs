using FortuneForge.Games.JadeDragon.Server;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Server.Controllers;

[ApiController]
[Route("api/slots/jade-dragon/presentation")]
[Route("api/slots/empty-slots/presentation")]
public sealed class JadeDragonPresentationController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(JadeDragonPresentation.Create());
}
