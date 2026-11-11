using System.Buffers.Binary;
using System.Security.Cryptography;
using FortuneForge.Games.Abstractions;
using FortuneForge.Games.TexasHoldem;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeModule
{
    internal static GameDescriptor Descriptor => TexasHoldemModule.Descriptor;

    internal static void Map(WebApplication app)
    {
        app.MapComputeAction("/internal/v1/tables/deal", (DealRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.Seats);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.MatchId);
            var tableRule = CreditHoldemTableRules.Resolve(request.TableRuleId);
            return CreditHoldemEngine.Deal(request.MatchId, request.Seats, CreditHoldemTableRules.Partition(tableRule.Id),
                GenerateSeed(), request.Now.UtcDateTime, tableRule.Id, request.UsesBuyInEscrow);
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/tables/next-hand", (NextHandRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.PrivateTable);
            ArgumentNullException.ThrowIfNull(request.Balances);
            return new
            {
                PrivateTable = CreditHoldemEngine.StartNextHand(request.PrivateTable, request.Balances,
                    GenerateSeed(), request.MinimumHumans, request.Now.UtcDateTime)
            };
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/tables/action", (ActionRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.PrivateTable);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ActorId);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Action);
            var wagerCents = CreditHoldemEngine.ApplyAction(request.PrivateTable, request.ActorId, request.Action,
                request.RaiseTo, request.Now.UtcDateTime);
            return new { request.PrivateTable, WagerCents = wagerCents };
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/tables/advance", (TableRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.PrivateTable);
            var advanced = CreditHoldemEngine.AdvanceExpiredTurn(request.PrivateTable, request.Now.UtcDateTime);
            return new { request.PrivateTable, Advanced = advanced };
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/tables/leave", (LeaveRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.PrivateTable);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.ActorId);
            CreditHoldemEngine.Leave(request.PrivateTable, request.ActorId, request.Now.UtcDateTime);
            return request.PrivateTable;
        }, isRuleViolation: IsRuleViolation);
        app.MapComputeAction("/internal/v1/tables/settle", (TableRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.PrivateTable);
            var settlement = CreditHoldemEngine.ApplyFinancialSettlement(request.PrivateTable, request.Now.UtcDateTime);
            return new { request.PrivateTable, Settlement = settlement };
        }, isRuleViolation: IsRuleViolation);
    }

    private static ulong GenerateSeed() => BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(sizeof(ulong)));

    private static bool IsRuleViolation(Exception error) => error is InvalidOperationException or
        CreditHoldemConflictException or CreditHoldemNotFoundException or CreditHoldemIllegalActionException;

    private sealed record DealRequest(string MatchId, CreditHoldemSeatAssignment[] Seats, DateTimeOffset Now,
        string TableRuleId = CreditHoldemTableRules.StandardId, bool UsesBuyInEscrow = false);
    private sealed record NextHandRequest(CreditHoldemMatch PrivateTable, IReadOnlyDictionary<string, long> Balances,
        int MinimumHumans, DateTimeOffset Now);
    private sealed record TableRequest(CreditHoldemMatch PrivateTable, DateTimeOffset Now);
    private sealed record ActionRequest(CreditHoldemMatch PrivateTable, string ActorId, string Action, int? RaiseTo, DateTimeOffset Now);
    private sealed record LeaveRequest(CreditHoldemMatch PrivateTable, string ActorId, DateTimeOffset Now);
}
