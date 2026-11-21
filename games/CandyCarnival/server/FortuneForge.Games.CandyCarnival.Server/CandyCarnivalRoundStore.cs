using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.CandyCarnival.Server;

public sealed class CandyCarnivalRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "candyCarnivalPreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<CandyCarnivalRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return CandyCarnivalRoundStateReply.From(before, pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<CandyCarnivalRoundResult> SpinAsync(string account, CandyCarnivalRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<CandyCarnivalRoundRequest, CandyCarnivalPreparedRound, CandyCarnivalRoundResult>(
            database, clock, CandyCarnivalRoundStateCodec.Storage, new(CandyCarnivalEngine.GameId, CandyCarnivalPaidRouteContract.AuditGameId,
                CandyCarnivalEngine.GameId, "Candy Carnival", RawCollectionName, CandyCarnivalRoundStateCodec.OwnerField))
    {
        protected override CandyCarnivalPreparedRound PrepareRound(string account, CandyCarnivalRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            CandyCarnivalRoundPolicy.Prepare(account, request, before, now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(CandyCarnivalPreparedRound round) => CandyCarnivalRoundPolicy.Validate(round);

        protected override CandyCarnivalPreparedRound RemoveObservation(CandyCarnivalPreparedRound round) => round with { Observation = null };

        protected override CandyCarnivalRoundResult CreateResult(CandyCarnivalPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? CandyCarnivalRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override CandyCarnivalRoundResult RestoreResult(CandyCarnivalRoundResult result, CandyCarnivalPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, CandyCarnivalPreparedRound round, CandyCarnivalRoundResult result,
            DateTime completed) => CandyCarnivalSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, CandyCarnivalRoundRequest? pending = null) =>
            new CandyCarnivalRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new CandyCarnivalRoundCooldownException(remaining);

        protected override Exception Paused() => new CandyCarnivalRoundPausedException();
    }
}
