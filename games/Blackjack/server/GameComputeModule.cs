using System.Security.Cryptography;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Blackjack;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => BlackjackModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapComputeAction("/internal/v1/tables/prepare-betting", (TableRequest request) =>
        {
            ValidateBettingTable(request.PrivateTable);
            BlackjackTableEngine.PrepareForBetting(request.PrivateTable);
            return request.PrivateTable;
        });
        app.MapComputeAction("/internal/v1/tables/begin-deal", (TimedTableRequest request) =>
        {
            ValidateBettingTable(request.PrivateTable);
            BlackjackTableEngine.BeginDeal(request.PrivateTable, BlackjackRules.CreateShuffledDeck(),
                BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong))), RequireUtc(request.NowUtc));
            return request.PrivateTable;
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/tables/advance", (TimedTableRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.PrivateTable);
            BlackjackTableEngine.AdvanceAutomatedTurns(request.PrivateTable, RequireUtc(request.NowUtc));
            return request.PrivateTable;
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/tables/action", (ActionRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.PrivateTable);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ActorId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Action);
            BlackjackTableEngine.ApplyAction(request.PrivateTable, request.ActorId, request.Action,
                RequireUtc(request.NowUtc));
            return request.PrivateTable;
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/tables/leave", (LeaveRequest request) =>
        {
            var player = FindPlayer(request.PrivateTable, request.ActorId);
            BlackjackTableEngine.MarkLeaving(request.PrivateTable, player, RequireUtc(request.NowUtc));
            return request.PrivateTable;
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/tables/legal-actions", (PlayerRequest request) =>
        {
            var player = FindPlayer(request.PrivateTable, request.ActorId);
            return BlackjackTableEngine.LegalActions(request.PrivateTable, player).Select(action => new
            {
                Action = action,
                AdditionalWagerCents = BlackjackTableEngine.AdditionalWagerFor(player, action)
            }).ToArray();
        }, isRuleViolation: IsRuleViolation);
    }

    private static void ValidateBettingTable(BlackjackTableState table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Phase != BlackjackTablePhases.Betting || !table.RoundAccountingSettled)
            throw new ArgumentException("A betting table with settled prior accounting is required.");
    }

    private static BlackjackTablePlayer FindPlayer(BlackjackTableState table, string actorId)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        return table.Players.SingleOrDefault(player => player.ActorId == actorId)
            ?? throw new BlackjackTableNotFoundException("The actor does not have a seat at this table.");
    }

    private static DateTime RequireUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : throw new ArgumentException("A UTC timestamp is required.");

    private static bool IsRuleViolation(Exception exception) => exception is
        BlackjackTableConflictException or BlackjackTableNotFoundException or
        BlackjackTableIllegalActionException;

    private sealed record TableRequest(BlackjackTableState PrivateTable);
    private sealed record TimedTableRequest(BlackjackTableState PrivateTable, DateTime NowUtc);
    private sealed record PlayerRequest(BlackjackTableState PrivateTable, string ActorId);
    private sealed record LeaveRequest(BlackjackTableState PrivateTable, string ActorId, DateTime NowUtc);
    private sealed record ActionRequest(BlackjackTableState PrivateTable, string ActorId, string Action,
        DateTime NowUtc);
}
