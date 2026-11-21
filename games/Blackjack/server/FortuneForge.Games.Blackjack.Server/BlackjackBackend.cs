namespace FortuneForge.Server.Cards.Blackjack;

public static class BlackjackBackend
{
    public const string Owner = "blackjack-worker-v1";
    public const string SoloGames = "blackjackGames";
    public const string SoloActions = "blackjackActions";
    public const string SoloCommands = "blackjackSoloWalletCommands";
    public const string TableState = "blackjackTableState";
    public const string Tables = "blackjackTables";
    public const string Tickets = "blackjackTableTickets";
    public const string Sessions = "blackjackTableSessions";
    public const string CommandGuards = "blackjackTableCommandGuards";
    public const string Leases = "blackjackTableLeases";
    public const string TableWalletCommands = "blackjackTableWalletCommands";
    public const string PendingShards = "blackjackTablePendingShards";
    public const string PendingAccounts = "blackjackTablePendingAccounts";
    public const string TableLedgerEvidence = "blackjackTableLedgerEvidence";
    public const string RoundRevenue = "blackjackTableRoundRevenue";
    public const string ManagedStatistics = "blackjackManagedPlayerStatistics";
}
