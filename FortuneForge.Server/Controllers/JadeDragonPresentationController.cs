using FortuneForge.Games.JadeDragon;
using Microsoft.AspNetCore.Mvc;
using Cascade = FortuneForge.Games.JadeDragon.JadeDragonCascadeRules;

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
        var definition = JadeDragonCascadeEngine.CreateDefinition();
        return Ok(new
        {
            gameId = JadeDragonEngine.GameId,
            engineVersion = Cascade.Version,
            title = "Jade Dragon",
            layout = definition.Layout,
            wagering = definition.Wagering,
            freeGames = definition.FreeGames,
            symbols = engine.SymbolMetadata,
            payoutScale = Cascade.PayoutScale,
            payoutPointValueInCents = Cascade.PayoutPointValueInCents,
            cascadeRules = new
            {
                ways = Cascade.Ways,
                baseMultipliers = Cascade.BaseMultipliers,
                freeSpinMultipliers = Cascade.BonusMultipliers,
                symbols = Cascade.Symbols.Select(symbol => new
                {
                    id = symbol.Id,
                    payouts = symbol.Pays?.Select(pay => pay * Cascade.PayoutScale).ToArray() ?? []
                }).ToArray(),
                requiredPearls = Cascade.RequiredPearls,
                baseFreeSpins = Cascade.BaseFreeSpins,
                retriggerFreeSpins = Cascade.RetriggerFreeSpins,
                spinsPerExtraPearl = Cascade.SpinsPerExtraPearl,
                maximumWinningSteps = Cascade.MaximumWinningSteps,
                maximumWinMultiplier = Cascade.MaximumWinMultiplier
            },
            collectionThreshold = 22,
            collectionSpins = 8,
            collectionFeatureMode = "copy-expand-wild",
            maximumPresentationBetCents = 10000
        });
    }
}
