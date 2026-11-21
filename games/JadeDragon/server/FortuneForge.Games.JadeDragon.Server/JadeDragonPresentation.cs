using Cascade = FortuneForge.Games.JadeDragon.JadeDragonCascadeRules;

namespace FortuneForge.Games.JadeDragon.Server;

public static class JadeDragonPresentation
{
    public static object Create()
    {
        var engine = new JadeDragonEngine();
        var definition = JadeDragonCascadeEngine.CreateDefinition();
        return new
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
            collectionThreshold = JadeDragonCollections.CollectionTarget,
            collectionSpins = JadeDragonCollections.AwardedSpins,
            collectionFeatureMode = JadeDragonCollections.CollectionFeatureMode,
            maximumPresentationBetCents = Cascade.MaximumPaidWagerPoints * Cascade.WagerPointValueInCents
        };
    }
}
