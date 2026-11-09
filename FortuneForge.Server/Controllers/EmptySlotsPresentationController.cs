using FortuneForge.Games.EmptySlots;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Server.Controllers;

/// <summary>Public display rules only. Never exposes reel strips or admits/settles wagers.</summary>
[ApiController]
[Route("api/slots/empty-slots/presentation")]
public sealed class EmptySlotsPresentationController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var engine = new EmptySlotsEngine();
        var definition = engine.Definition;
        return Ok(new
        {
            gameId = EmptySlotsEngine.GameId,
            engineVersion = EmptySlotsEngine.Version,
            title = "Jade Dragon: The Golden Pearl",
            layout = definition.Layout,
            wagering = definition.Wagering,
            freeGames = definition.FreeGames,
            symbols = engine.SymbolMetadata,
            // Native multipliers are displayed with the engine's scale/rounding explanation.
            payoutScale = definition.Math.PayoutMultiplier,
            collectionThreshold = 22,
            collectionSpins = 8,
            collectionFeatureMode = "copy-expand-wild",
            maximumPresentationBetCents = 10000
        });
    }
}
