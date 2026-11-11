using System.Security.Cryptography;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.ThreeCardPoker;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => ThreeCardPokerModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapComputeAction("/internal/v1/tables/prepare-betting", (TableRequest request) =>
        {
            ValidateBettingTable(request.PrivateTable);
            ThreeCardPokerTableEngine.PrepareForBetting(request.PrivateTable);
            return request.PrivateTable;
        });
        app.MapComputeAction("/internal/v1/tables/begin-deal", (TimedTableRequest request) =>
        {
            ValidateBettingTable(request.PrivateTable);
            ThreeCardPokerTableEngine.BeginDeal(request.PrivateTable, ThreeCardPokerRules.CreateShuffledDeck(),
                BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))), RequireUtc(request.NowUtc));
            return request.PrivateTable;
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/tables/advance", (TimedTableRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.PrivateTable);
            ThreeCardPokerTableEngine.AdvanceAutomatedTurns(request.PrivateTable, RequireUtc(request.NowUtc));
            return request.PrivateTable;
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/tables/action", (ActionRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.PrivateTable);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ActorId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Action);
            ThreeCardPokerTableEngine.ApplyAction(request.PrivateTable, request.ActorId, request.Action,
                RequireUtc(request.NowUtc));
            return request.PrivateTable;
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/tables/leave", (LeaveRequest request) =>
        {
            var player = FindPlayer(request.PrivateTable, request.ActorId);
            ThreeCardPokerTableEngine.MarkLeaving(request.PrivateTable, player, RequireUtc(request.NowUtc));
            return request.PrivateTable;
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/tables/legal-actions", (PlayerRequest request) =>
        {
            var player = FindPlayer(request.PrivateTable, request.ActorId);
            return ThreeCardPokerTableEngine.LegalActions(request.PrivateTable, player).Select(action => new
            {
                Action = action,
                AdditionalWagerCents = ThreeCardPokerTableEngine.AdditionalWagerFor(player, action)
            }).ToArray();
        }, isRuleViolation: IsRuleViolation);
    }

    private static void ValidateBettingTable(ThreeCardPokerTableState table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Phase != ThreeCardPokerTablePhases.Betting || !table.RoundAccountingSettled)
            throw new ArgumentException("A betting table with settled prior accounting is required.");
    }

    private static ThreeCardPokerTablePlayer FindPlayer(ThreeCardPokerTableState table, string actorId)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        return table.Players.SingleOrDefault(player => player.ActorId == actorId)
            ?? throw new ThreeCardPokerTableNotFoundException("The actor does not have a seat at this table.");
    }

    private static DateTime RequireUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : throw new ArgumentException("A UTC timestamp is required.");

    private static bool IsRuleViolation(Exception exception) => exception is
        ThreeCardPokerTableConflictException or ThreeCardPokerTableNotFoundException or
        ThreeCardPokerTableIllegalActionException;

    private sealed record TableRequest(ThreeCardPokerTableState PrivateTable);
    private sealed record TimedTableRequest(ThreeCardPokerTableState PrivateTable, DateTime NowUtc);
    private sealed record PlayerRequest(ThreeCardPokerTableState PrivateTable, string ActorId);
    private sealed record LeaveRequest(ThreeCardPokerTableState PrivateTable, string ActorId, DateTime NowUtc);
    private sealed record ActionRequest(ThreeCardPokerTableState PrivateTable, string ActorId, string Action,
        DateTime NowUtc);
}
