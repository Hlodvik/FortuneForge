using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Builder;

namespace FortuneForge.Server.Cards.TexasHoldem.Credit;

public static class CreditHoldemComputeEndpoints
{
    public static void MapCreditHoldemCompute(this WebApplication app)
    {
        app.MapGameAction("/internal/v1/tables/deal", Rules((DealRequest request) =>
        {
            ArgumentNullException.ThrowIfNull(request.Seats);
            return CreditHoldemEngine.Deal(request.MatchId, request.Seats, request.PartitionKey, request.Seed,
                request.Now.UtcDateTime, CreditHoldemTableRules.Resolve(request.TableRuleId).Id, request.UsesBuyInEscrow);
        }));
        app.MapGameAction("/internal/v1/tables/next-hand", Rules((NextHandRequest request) => new
        {
            PrivateTable = CreditHoldemEngine.StartNextHand(request.PrivateTable, request.Balances, request.Seed,
                request.MinimumHumans, request.Now.UtcDateTime)
        }));
        app.MapGameAction("/internal/v1/tables/action", Rules((ActionRequest request) =>
        {
            var wagerCents = CreditHoldemEngine.ApplyAction(request.PrivateTable, request.ActorId, request.Action,
                request.RaiseTo, request.Now.UtcDateTime);
            return new { request.PrivateTable, WagerCents = wagerCents };
        }));
        app.MapGameAction("/internal/v1/tables/advance", Rules((TableRequest request) =>
        {
            var advanced = CreditHoldemEngine.AdvanceExpiredTurn(request.PrivateTable, request.Now.UtcDateTime);
            return new { request.PrivateTable, Advanced = advanced };
        }));
        app.MapGameAction("/internal/v1/tables/leave", Rules((LeaveRequest request) =>
        {
            CreditHoldemEngine.Leave(request.PrivateTable, request.ActorId, request.Now.UtcDateTime);
            return request.PrivateTable;
        }));
        app.MapGameAction("/internal/v1/tables/settle", Rules((TableRequest request) =>
        {
            var settlement = CreditHoldemEngine.ApplyFinancialSettlement(request.PrivateTable, request.Now.UtcDateTime);
            return new { request.PrivateTable, Settlement = settlement };
        }));
    }

    private static Func<TRequest, TResult> Rules<TRequest, TResult>(Func<TRequest, TResult> rule) => request =>
    {
        try { return rule(request); }
        catch (Exception error) when (error is InvalidOperationException or CreditHoldemConflictException or
            CreditHoldemNotFoundException or CreditHoldemIllegalActionException)
        {
            throw new ArgumentException(error.Message, error);
        }
    };

    private sealed record DealRequest(string MatchId, CreditHoldemSeatAssignment[] Seats, string PartitionKey, ulong Seed,
        DateTimeOffset Now, string TableRuleId = CreditHoldemTableRules.StandardId, bool UsesBuyInEscrow = false);
    private sealed record NextHandRequest(CreditHoldemMatch PrivateTable, IReadOnlyDictionary<string, long> Balances,
        ulong Seed, int MinimumHumans, DateTimeOffset Now);
    private sealed record TableRequest(CreditHoldemMatch PrivateTable, DateTimeOffset Now);
    private sealed record ActionRequest(CreditHoldemMatch PrivateTable, string ActorId, string Action, int? RaiseTo, DateTimeOffset Now);
    private sealed record LeaveRequest(CreditHoldemMatch PrivateTable, string ActorId, DateTimeOffset Now);
}
