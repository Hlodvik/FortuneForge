using FortuneForge.Server.Slots.Bonuses;
using FortuneForge.Server.Slots.Games;
using FortuneForge.Server.Slots.Games.GoldFoundry;
using FortuneForge.Server.Slots.Models;
using Microsoft.Extensions.Options;

namespace FortuneForge.Server.Slots.Configuration;

/// <summary>
/// Host definition adapter. Playable slot definitions come from first-class
/// game packages; appsettings no longer owns any game math.
/// </summary>
public sealed class OptionsSlotsDefinitionProvider : ISlotsDefinitionProvider
{
    public OptionsSlotsDefinitionProvider() { }

    // Source-compatible bridge for tools and historical tests. Package-owned
    // definitions intentionally ignore host configuration.
    public OptionsSlotsDefinitionProvider(IOptions<SlotsOptions> _) { }

    public GameDefinition? GetGame(string id)
    {
        if (id == SlotSpecialRoundProfiles.JadeDragonGameId)
            return Games.JadeDragon.JadeDragonEngine.CreateGame();
        if (id == NeonArcadeSlotDefinitions.GameId)
            return NeonArcadeSlotDefinitions.CreateGame();
        if (id == MidnightVaultSlotDefinitions.GameId)
            return MidnightVaultSlotDefinitions.CreateGame();
        if (id == GoldFoundrySlotDefinitions.GameId)
            return GoldFoundrySlotDefinitions.CreateGame();
        return StandaloneSlotPackageRegistry.GetGame(id);
    }

    public SymbolSetDefinition? GetSymbolSet(string id)
    {
        if (id == "empty-slots-symbols-v1")
            return Games.JadeDragon.JadeDragonEngine.CreateSymbolSet();
        if (id == GoldFoundrySlotDefinitions.SymbolSetId)
            return GoldFoundrySlotDefinitions.CreateSymbolSet();
        if (id == NeonArcadeSlotDefinitions.SymbolSetId)
            return NeonArcadeSlotDefinitions.CreateSymbolSet();
        if (id == NeonArcadeSlotDefinitions.CarSymbolSetId)
            return NeonArcadeSlotDefinitions.CreateCarSymbolSet();
        if (id == MidnightVaultSlotDefinitions.SymbolSetId)
            return MidnightVaultSlotDefinitions.CreateSymbolSet();
        return StandaloneSlotPackageRegistry.GetSymbolSet(id);
    }

    public ReelSetDefinition? GetReelSet(string id)
    {
        if (id == "empty-slots-reels-v1")
            return Games.JadeDragon.JadeDragonEngine.CreateReelSet();
        if (id == GoldFoundrySlotDefinitions.ReelSetId)
            return GoldFoundrySlotDefinitions.CreateReelSet();
        if (id == NeonArcadeSlotDefinitions.ReelSetId)
            return NeonArcadeSlotDefinitions.CreateReelSet();
        if (id == NeonArcadeSlotDefinitions.CarReelSetId)
            return NeonArcadeSlotDefinitions.CreateCarReelSet();
        if (id == MidnightVaultBaseReelSet.Id)
            return MidnightVaultBaseReelSet.Create();
        if (id == MidnightVaultBaseReelSet.CurrentId)
            return MidnightVaultBaseReelSet.CreateCurrent();
        return StandaloneSlotPackageRegistry.GetReelSet(id);
    }

    public PaytableDefinition? GetPaytable(string id)
    {
        if (id == "empty-slots-paytable-v1")
            return Games.JadeDragon.JadeDragonEngine.CreatePaytable();
        if (id == GoldFoundrySlotDefinitions.PaytableId)
            return GoldFoundrySlotDefinitions.CreatePaytable();
        if (id == NeonArcadeSlotDefinitions.PaytableId)
            return NeonArcadeSlotDefinitions.CreatePaytable();
        if (id == NeonArcadeSlotDefinitions.CarPaytableId)
            return NeonArcadeSlotDefinitions.CreateCarPaytable();
        if (id == NeonArcadeSlotDefinitions.PreviousPaytableId)
            return NeonArcadeSlotDefinitions.CreatePreviousPaytable();
        if (id == MidnightVaultSlotDefinitions.PaytableId)
            return MidnightVaultSlotDefinitions.CreatePaytable();
        if (id == MidnightVaultSlotDefinitions.PreviousPaytableId)
            return MidnightVaultSlotDefinitions.CreatePreviousPaytable();
        if (id == MidnightVaultSlotDefinitions.LegacyPaytableId)
            return MidnightVaultSlotDefinitions.CreateLegacyPaytable();
        return StandaloneSlotPackageRegistry.GetPaytable(id);
    }
}
