using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.CrookedRoad.Server;

public static class CrookedRoadRoundStateCodec
{
    public const string OwnerField = "crookedRoadRoundOwner";
    internal static ClassicSlotRoundStateCodec Storage { get; } = new(CrookedRoadRules.GameId, OwnerField,
        CrookedRoadRules.GameId, "crookedRoadRoundState", "The Crooked Road", false, CrookedRoadRules.PointValueInCents,
        wager => CrookedRoadRules.ValidateWager(wager, true), native => _ = CrookedRoadState.Parse(native));
}

public sealed class CrookedRoadRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "crookedRoadPreparedRounds";
    internal static readonly ClassicSlotPaidRoundStoreOptions WalletOptions = new(TracksLegacyFreeGames: false,
        TransactionIdLayout: GameWalletTransactionIdLayout.StandardSettlement);
    private readonly Persistence persistence = new(database, random, clock);
    internal async Task<CrookedRoadStateReply> ReadAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pending) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return CrookedRoadStateReply.From(CrookedRoadState.Parse(before.NativeStateJson), wallet.SlotsCreditsBalanceCents, pending);
    }
    internal Task<CrookedRoadRoundResult> SpinAsync(string account, CrookedRoadRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart || request.UseFreeSpin, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<CrookedRoadRoundRequest, CrookedRoadPreparedRound, CrookedRoadRoundResult>(database,
            clock, CrookedRoadRoundStateCodec.Storage, new(CrookedRoadRules.GameId, CrookedRoadPaidRouteContract.AuditGameId,
                CrookedRoadRules.GameId, "The Crooked Road", RawCollectionName, CrookedRoadRoundStateCodec.OwnerField),
            WalletOptions)
    {
        protected override CrookedRoadPreparedRound PrepareRound(string account, CrookedRoadRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing)
        {
            if (request.UseSpecialBoost) throw new ArgumentException("The Crooked Road has no purchased special boost.");
            if (before.LegacyRemaining != 0 || before.NativeRemaining != 0 || before.Carry is not null)
                throw new InvalidOperationException("Unrecognized Crooked Road entitlement.");
            var tape = new CrookedRoadTape(random);
            var outcome = CrookedRoadGame.Spin(CrookedRoadState.Parse(before.NativeStateJson), request.WagerPoints,
                request.UseFreeSpin, Guid.NewGuid(), now, tape);
            var after = before with { NativeStateJson = CrookedRoadState.Serialize(outcome.After) };
            var round = new CrookedRoadPreparedRound(Guid.NewGuid(), account, request, before, after, outcome,
                tape.Finish(), CrookedRoadEvidence.Hash(outcome), request.UseFreeSpin ? 0 : request.WagerPoints,
                outcome.Payout.TotalPoints, now, observation, importing);
            CrookedRoadEvidence.Validate(round);
            return round;
        }
        protected override void ValidateRound(CrookedRoadPreparedRound round)
        {
            try { CrookedRoadEvidence.Validate(round); }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or OverflowException)
            { throw new GameWalletUnavailableException(exception); }
        }
        protected override CrookedRoadPreparedRound RemoveObservation(CrookedRoadPreparedRound round) => round with { Observation = null };
        protected override CrookedRoadRoundResult CreateResult(CrookedRoadPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? CrookedRoadStateReply.From(round.Outcome.After, receipt.BalanceAfterCents) : null, receipt, round.WagerCents)
            { Round = settled ? round : null };
        protected override CrookedRoadRoundResult RestoreResult(CrookedRoadRoundResult result, CrookedRoadPreparedRound? round) => result with { Round = round };
        protected override void Report(Transaction transaction, FirestoreDb database, CrookedRoadPreparedRound round, CrookedRoadRoundResult result,
            DateTime completed) => CrookedRoadReport.Write(transaction, database, round, result.Wallet, completed);
        protected override Exception Conflict(string message, CrookedRoadRoundRequest? pending = null) => new CrookedRoadPendingException(message, pending);
        protected override Exception CoolingDown(TimeSpan remaining) => new CrookedRoadCooldownException(remaining);
        protected override Exception Paused() => new CrookedRoadConflictException("slot-prepared-start-paused", "New Crooked Road wagers are paused.");
    }
}

public sealed class CrookedRoadPendingException(string message, CrookedRoadRoundRequest? pending) : InvalidOperationException(message)
{
    public CrookedRoadRoundRequest? PendingOperation { get; } = pending;
}
public sealed class CrookedRoadCooldownException(TimeSpan remaining) : InvalidOperationException("Wait for the previous play to settle.")
{
    public TimeSpan Remaining { get; } = remaining;
}
