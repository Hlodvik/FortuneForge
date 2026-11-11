using FortuneForge.Games.Slots;

namespace FortuneForge.Games.MidnightVault;

/// <summary>
/// Reel-generation port used by the standalone engine. Hosts may supply their
/// existing generator without moving Midnight Vault's rules back into the host.
/// </summary>
public interface IMidnightVaultReelSource
{
    ReelOutcome Generate(GameDefinition game, ReelSetDefinition reelSet, SymbolSetDefinition symbolSet);
}
