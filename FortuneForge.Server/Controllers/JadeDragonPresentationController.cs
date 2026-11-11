using FortuneForge.Games.JadeDragon;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Server.Controllers;

/// <summary>Public display rules only. Never exposes reel strips or admits or settles wagers.</summary>
[ApiController]
[Route("api/slots/jade-dragon/presentation")]
[Route("api/slots/empty-slots/presentation")]
public sealed class JadeDragonPresentationController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var engine = new JadeDragonEngine();
        var definition = engine.Definition;
        return Ok(new
        {
            gameId = JadeDragonEngine.GameId,
            engineVersion = JadeDragonEngine.Version,
            title = "Jade Dragon: The Golden Pearl",
            layout = definition.Layout,
            wagering = definition.Wagering,
            freeGames = definition.FreeGames,
            symbols = engine.SymbolMetadata,
            payoutScale = definition.Math.PayoutMultiplier,
            collectionThreshold = 22,
            collectionSpins = 8,
            collectionFeatureMode = "copy-expand-wild",
            maximumPresentationBetCents = 10000
        });
    }
}
