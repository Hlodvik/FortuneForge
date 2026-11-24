using FortuneForge.Games.GodsOfOlympus;
using FortuneForge.Games.GodsOfOlympus.Server;
using FortuneForge.Games.GoldFoundry;
using FortuneForge.Games.GoldFoundry.Server;
using FortuneForge.Games.FrontierFortune;
using FortuneForge.Games.FrontierFortune.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.JadeDragon;
using FortuneForge.Games.JadeDragon.Server;
using FortuneForge.Games.MidnightVault;
using FortuneForge.Games.MidnightVault.Server;
using FortuneForge.Games.NeonNights.Server;
using FortuneForge.Games.PiratesFortune;
using FortuneForge.Games.PiratesFortune.Server;
using FortuneForge.Games.RainbowRealm;
using FortuneForge.Games.RainbowRealm.Server;
using FortuneForge.Games.ReelRiches;
using FortuneForge.Games.ReelRiches.Server;
using FortuneForge.Games.RoyalDraw;
using FortuneForge.Games.RoyalDraw.Server;
using FortuneForge.Games.CosmicFortune;
using FortuneForge.Games.CosmicFortune.Server;
using FortuneForge.Games.OceanOdyssey;
using FortuneForge.Games.OceanOdyssey.Server;
using FortuneForge.Games.CrookedRoad;
using FortuneForge.Games.CrookedRoad.Server;
using FortuneForge.Games.CandyCarnival;
using FortuneForge.Games.CandyCarnival.Server;
using FortuneForge.Games.PhantomManor;
using FortuneForge.Games.PhantomManor.Server;
using FortuneForge.Games.NordicLegends;
using FortuneForge.Games.NordicLegends.Server;
using FortuneForge.Games.DesertTreasures;
using FortuneForge.Games.DesertTreasures.Server;
using FortuneForge.Games.RobotRevolution;
using FortuneForge.Games.RobotRevolution.Server;
using FortuneForge.Games.Wukong;
using FortuneForge.Games.Wukong.Server;
using FortuneForge.Server.Slots.Configuration;

namespace FortuneForge.Server.GameWorkers;

internal sealed record PreparedSlotWorkerRoute(string EnabledSetting, string SpinPath, string StatePath, string LegacyOwnerField);

internal static class PreparedSlotWorkerRegistry
{
    internal const string PublicSpinRoute = PreparedSlotSpinRoutes.Segment;
    internal const string PublicSpinPath = PreparedSlotSpinRoutes.Path;

    internal static bool IsPreparedPath(PathString path) =>
        string.Equals(path.Value?.TrimEnd('/'), PublicSpinPath, StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string, PreparedSlotWorkerRoute> Routes = new(StringComparer.Ordinal)
    {
        [NeonArcadeSlotDefinitions.GameId] = new("Slots:NeonWorkerRoundsEnabled", NeonPrimaryRouteContract.SpinPath,
            NeonPrimaryRouteContract.StatePath, NeonCarRespinStore.WorkerOwnerField),
        [JadeDragonEngine.GameId] = new("Slots:JadeDragonWorkerRoundsEnabled", JadePaidRouteContract.SpinPath,
            JadePaidRouteContract.StatePath, JadeRoundStateCodec.OwnerField),
        [GoldFoundryEngine.GameId] = new("Slots:GoldFoundryWorkerRoundsEnabled", GoldFoundryPaidRouteContract.SpinPath,
            GoldFoundryPaidRouteContract.StatePath, GoldFoundryRoundStateCodec.OwnerField),
        [MidnightVaultEngine.GameId] = new("Slots:MidnightVaultWorkerRoundsEnabled", MidnightVaultPaidRouteContract.SpinPath,
            MidnightVaultPaidRouteContract.StatePath, MidnightVaultRoundStateCodec.OwnerField),
        [WukongEngine.GameId] = new("Slots:WukongWorkerRoundsEnabled", WukongPaidRouteContract.SpinPath,
            WukongPaidRouteContract.StatePath, WukongRoundStateCodec.OwnerField),
        [RainbowRealmEngine.GameId] = new("Slots:RainbowRealmWorkerRoundsEnabled", RainbowPaidRouteContract.SpinPath,
            RainbowPaidRouteContract.StatePath, RainbowRoundStateCodec.OwnerField),
        [PiratesFortuneEngine.GameId] = new("Slots:PiratesFortuneWorkerRoundsEnabled", PiratesPaidRouteContract.SpinPath,
            PiratesPaidRouteContract.StatePath, PiratesRoundStateCodec.OwnerField),
        [GodsOfOlympusEngine.GameId] = new("Slots:GodsOfOlympusWorkerRoundsEnabled", OlympusPaidRouteContract.SpinPath,
            OlympusPaidRouteContract.StatePath, OlympusRoundStateCodec.OwnerField),
        [ReelRichesEngine.GameId] = new("Slots:ReelRichesWorkerRoundsEnabled", ReelRichesPaidRouteContract.SpinPath,
            ReelRichesPaidRouteContract.StatePath, ReelRichesRoundStateCodec.OwnerField),
        [FrontierFortuneEngine.GameId] = new("Slots:FrontierFortuneWorkerRoundsEnabled", FrontierPaidRouteContract.SpinPath,
            FrontierPaidRouteContract.StatePath, FrontierRoundStore.OwnerField),
        [RoyalDrawEngine.GameId] = new("Slots:RoyalDrawWorkerRoundsEnabled", RoyalDrawPaidRouteContract.SpinPath,
            RoyalDrawPaidRouteContract.StatePath, RoyalDrawRoundStateCodec.OwnerField),
        [CosmicFortuneEngine.GameId] = new("Slots:CosmicFortuneWorkerRoundsEnabled", CosmicFortunePaidRouteContract.SpinPath,
            CosmicFortunePaidRouteContract.StatePath, CosmicFortuneRoundStateCodec.OwnerField),
        [OceanOdysseyEngine.GameId] = new("Slots:OceanOdysseyWorkerRoundsEnabled", OceanOdysseyPaidRouteContract.SpinPath,
            OceanOdysseyPaidRouteContract.StatePath, OceanOdysseyRoundStateCodec.OwnerField),
        [CrookedRoadRules.GameId] = new("Slots:CrookedRoadWorkerRoundsEnabled", CrookedRoadPaidRouteContract.SpinPath,
            CrookedRoadPaidRouteContract.StatePath, CrookedRoadRoundStateCodec.OwnerField),
        [CandyCarnivalEngine.GameId] = new("Slots:CandyCarnivalWorkerRoundsEnabled", CandyCarnivalPaidRouteContract.SpinPath,
            CandyCarnivalPaidRouteContract.StatePath, CandyCarnivalRoundStateCodec.OwnerField),
        [PhantomManorEngine.GameId] = new("Slots:PhantomManorWorkerRoundsEnabled", PhantomManorPaidRouteContract.SpinPath,
            PhantomManorPaidRouteContract.StatePath, PhantomManorRoundStateCodec.OwnerField),
        [NordicLegendsEngine.GameId] = new("Slots:NordicLegendsWorkerRoundsEnabled", NordicLegendsPaidRouteContract.SpinPath,
            NordicLegendsPaidRouteContract.StatePath, NordicLegendsRoundStateCodec.OwnerField),
        [DesertTreasuresEngine.GameId] = new("Slots:DesertTreasuresWorkerRoundsEnabled", DesertTreasuresPaidRouteContract.SpinPath,
            DesertTreasuresPaidRouteContract.StatePath, DesertTreasuresRoundStateCodec.OwnerField),
        [RobotRevolutionEngine.GameId] = new("Slots:RobotRevolutionWorkerRoundsEnabled", RobotRevolutionPaidRouteContract.SpinPath,
            RobotRevolutionPaidRouteContract.StatePath, RobotRevolutionRoundStateCodec.OwnerField)
    };

    internal static PreparedSlotWorkerRoute? Find(string gameId) => Routes.GetValueOrDefault(gameId);
}
