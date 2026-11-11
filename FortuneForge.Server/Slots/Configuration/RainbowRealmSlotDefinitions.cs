using FortuneForge.Server.Slots.Models;

namespace FortuneForge.Server.Slots.Configuration;

internal static class RainbowRealmSlotDefinitions
{
    internal const string EngineVersion = FortuneForge.Games.RainbowRealm.RainbowRealmEngine.EngineVersion;
    internal const string SymbolSetId = FortuneForge.Games.RainbowRealm.RainbowRealmEngine.SymbolSetId;
    internal const string ReelSetId = FortuneForge.Games.RainbowRealm.RainbowRealmEngine.ReelSetId;
    internal const string PaytableId = FortuneForge.Games.RainbowRealm.RainbowRealmEngine.PaytableId;

    internal static bool IsModel(GameDefinition game) =>
        game.Id == FortuneForge.Games.RainbowRealm.RainbowRealmEngine.GameId &&
        game.Math.PaytableId == PaytableId;
}
