using FortuneForge.Games.Hosting;
using System.Security.Cryptography;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.Blackjack;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => BlackjackModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapGameAction("/internal/v1/solo/deal", (SoloDealRequest request) =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request.GameId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.WagerCents);
            return BlackjackRules.Deal(request.GameId, request.UserId, request.WagerCents,
                BlackjackRules.CreateShuffledDeck(), RequireUtc(request.NowUtc));
        }, request => request.UserId);
        app.MapGameAction("/internal/v1/solo/action", (SoloActionRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.Game);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Action);
            try { return BlackjackRules.ApplyAction(request.Game, request.Action, RequireUtc(request.NowUtc)); }
            catch (BlackjackConflictException exception) { throw new ArgumentException(exception.Message, exception); }
        }, request => request.Game?.UserId ?? "");

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

    private sealed record SoloDealRequest(string GameId, string UserId, long WagerCents, DateTime NowUtc);
    private sealed record SoloActionRequest(BlackjackGame Game, string Action, DateTime NowUtc);
    private sealed record TableRequest(BlackjackTableState PrivateTable);
    private sealed record TimedTableRequest(BlackjackTableState PrivateTable, DateTime NowUtc);
    private sealed record PlayerRequest(BlackjackTableState PrivateTable, string ActorId);
    private sealed record LeaveRequest(BlackjackTableState PrivateTable, string ActorId, DateTime NowUtc);
    private sealed record ActionRequest(BlackjackTableState PrivateTable, string ActorId, string Action,
        DateTime NowUtc);
}
