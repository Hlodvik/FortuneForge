namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

public static class CreditHoldemBackend
{
    public const string GameId = TexasHoldemModule.GameId;
    public const string ApiPath = TexasHoldemModule.ApiPath;
    public const string EnabledSetting = "Features:CreditTexasHoldemEnabled";
    internal const string Owner = "texas-holdem-package-v1";
    internal const string Mutations = "creditHoldemPreparedMutations";
    internal const string Locks = "creditHoldemMutationLocks";
    internal const string WalletJournal = "creditHoldemWalletJournal";
    internal const string Sessions = "creditHoldemSessions";
    internal const string Tickets = "creditHoldemTickets";
    internal const string QueuePartitions = "creditHoldemQueuePartitions";
    internal const string Matches = "creditHoldemMatches";
    internal const string CommandGuards = "creditHoldemCommandGuards";
    internal const string MatchRevenue = "creditHoldemMatchRevenue";
    internal const string PlayerHistory = "creditHoldemPlayerHistory";
    internal const string RosterOutbox = "creditHoldemRosterOutbox";
}
