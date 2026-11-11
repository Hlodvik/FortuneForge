using FortuneForge.Server.Slots.Models;
using PackageDefinitions = FortuneForge.Games.GoldFoundry.GoldFoundrySlotDefinitions;

namespace FortuneForge.Server.Slots.Games.GoldFoundry;

// Source-compatible host adapter. The versioned data and validation rules live
// in the standalone Gold Foundry package.
public static class GoldFoundrySlotDefinitions
{
    public const string GameId = PackageDefinitions.GameId;
    public const string SymbolSetId = PackageDefinitions.SymbolSetId;
    public const string ReelSetId = PackageDefinitions.ReelSetId;
    public const string PaytableId = PackageDefinitions.PaytableId;

    public static bool IsModel(GameDefinition game) => PackageDefinitions.IsModel(game);
    internal static void ValidateSpinRequest(bool useFreeSpin) =>
        PackageDefinitions.ValidateSpinRequest(useFreeSpin);
    public static GameDefinition CreateGame() => PackageDefinitions.CreateGame();
    public static SymbolSetDefinition CreateSymbolSet() => PackageDefinitions.CreateSymbolSet();
    public static ReelSetDefinition CreateReelSet() => PackageDefinitions.CreateReelSet();
    public static PaytableDefinition CreatePaytable() => PackageDefinitions.CreatePaytable();
}
