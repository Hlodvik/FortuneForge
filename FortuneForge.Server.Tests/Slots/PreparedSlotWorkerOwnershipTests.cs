using FortuneForge.Games.GodsOfOlympus;
using FortuneForge.Games.GodsOfOlympus.Server;
using FortuneForge.Games.FrontierFortune;
using FortuneForge.Games.FrontierFortune.Server;
using FortuneForge.Games.PiratesFortune;
using FortuneForge.Games.PiratesFortune.Server;
using FortuneForge.Games.RainbowRealm;
using FortuneForge.Games.RainbowRealm.Server;
using FortuneForge.Games.ReelRiches;
using FortuneForge.Games.ReelRiches.Server;
using FortuneForge.Games.RoyalDraw;
using FortuneForge.Games.RoyalDraw.Server;
using FortuneForge.Games.PharaohsAscent;
using FortuneForge.Games.PharaohsAscent.Server;
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
using FortuneForge.Games.Slots;
using FortuneForge.Games.Wukong;
using FortuneForge.Games.Wukong.Server;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.GameWorkers;
using Xunit;

namespace FortuneForge.Server.Tests.Slots;

[Collection(MidnightVaultPaidEmulatorCollection.Name)]
public sealed class PreparedSlotWorkerOwnershipTests(MidnightVaultPaidEmulatorFixture emulator)
{
    [Theory]
    [InlineData(WukongEngine.GameId, WukongEngine.LegacyStorageGameId, WukongRoundStateCodec.OwnerField)]
    [InlineData(RainbowRealmEngine.GameId, RainbowRealmEngine.GameId, RainbowRoundStateCodec.OwnerField)]
    [InlineData(PiratesFortuneEngine.GameId, PiratesFortuneEngine.GameId, PiratesRoundStateCodec.OwnerField)]
    [InlineData(GodsOfOlympusEngine.GameId, GodsOfOlympusEngine.GameId, OlympusRoundStateCodec.OwnerField)]
    [InlineData(ReelRichesEngine.GameId, ReelRichesEngine.GameId, ReelRichesRoundStateCodec.OwnerField)]
    [InlineData(FrontierFortuneEngine.GameId, FrontierFortuneEngine.GameId, FrontierRoundStore.OwnerField)]
    [InlineData(RoyalDrawEngine.GameId, RoyalDrawEngine.GameId, RoyalDrawRoundStateCodec.OwnerField)]
    [InlineData(PharaohsAscentEngine.GameId, PharaohsAscentEngine.GameId, PharaohsAscentRoundStateCodec.OwnerField)]
    [InlineData(OceanOdysseyEngine.GameId, OceanOdysseyEngine.GameId, OceanOdysseyRoundStateCodec.OwnerField)]
    [InlineData(CrookedRoadRules.GameId, CrookedRoadRules.GameId, CrookedRoadRoundStateCodec.OwnerField)]
    [InlineData(CandyCarnivalEngine.GameId, CandyCarnivalEngine.GameId, CandyCarnivalRoundStateCodec.OwnerField)]
    [InlineData(PhantomManorEngine.GameId, PhantomManorEngine.GameId, PhantomManorRoundStateCodec.OwnerField)]
    [InlineData(NordicLegendsEngine.GameId, NordicLegendsEngine.GameId, NordicLegendsRoundStateCodec.OwnerField)]
    [InlineData(DesertTreasuresEngine.GameId, DesertTreasuresEngine.GameId, DesertTreasuresRoundStateCodec.OwnerField)]
    [InlineData(RobotRevolutionEngine.GameId, RobotRevolutionEngine.GameId, RobotRevolutionRoundStateCodec.OwnerField)]
    public async Task LegacyReadAndAdmissionStayClosedAfterWorkerAdoptsPlayer(string gameId, string storageGameId, string ownerField)
    {
        const string player = "prepared-slot-owner-test";
        var database = emulator.Database();
        var store = new FirestoreAccountStore(database);
        await database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(player, storageGameId))
            .CreateAsync(new Dictionary<string, object> { [ownerField] = gameId });

        await Assert.ThrowsAsync<GameWorkerUnavailableException>(() => store.GetSlotStateAsync(player, gameId, 25, default));
        await Assert.ThrowsAsync<GameWorkerUnavailableException>(() => store.BeginSlotSpinAsync(player, gameId, 12, 25,
            false, false, 0, DateTime.UtcNow, TimeSpan.Zero, default));

        var otherGame = gameId == WukongEngine.GameId ? RainbowRealmEngine.GameId : WukongEngine.GameId;
        Assert.Equal(0, (await store.GetSlotStateAsync(player, otherGame, 25, default)).FreeSpinsRemaining);
        Assert.Empty((await database.Collection("slotSpinAdmissions").GetSnapshotAsync()).Documents);
    }
}
