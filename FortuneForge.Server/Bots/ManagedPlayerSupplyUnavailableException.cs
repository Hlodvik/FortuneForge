namespace FortuneForge.Server.Bots;

/// <summary>A fresh practice table cannot open without its required opponents.</summary>
public sealed class ManagedPlayerSupplyUnavailableException() : Exception(
    "No new automated opponents are available for this game right now. Existing games can continue.");
