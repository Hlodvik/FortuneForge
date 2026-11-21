using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.PhantomManor.Server;

public sealed class PhantomManorRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "phantomManorPreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<PhantomManorRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return PhantomManorRoundStateReply.From(before, pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<PhantomManorRoundResult> SpinAsync(string account, PhantomManorRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<PhantomManorRoundRequest, PhantomManorPreparedRound, PhantomManorRoundResult>(
            database, clock, PhantomManorRoundStateCodec.Storage, new(PhantomManorEngine.GameId, PhantomManorPaidRouteContract.AuditGameId,
                PhantomManorEngine.GameId, "Phantom Manor", RawCollectionName, PhantomManorRoundStateCodec.OwnerField))
    {
        protected override PhantomManorPreparedRound PrepareRound(string account, PhantomManorRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            PhantomManorRoundPolicy.Prepare(account, request, before, now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(PhantomManorPreparedRound round) => PhantomManorRoundPolicy.Validate(round);

        protected override PhantomManorPreparedRound RemoveObservation(PhantomManorPreparedRound round) => round with { Observation = null };

        protected override PhantomManorRoundResult CreateResult(PhantomManorPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? PhantomManorRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override PhantomManorRoundResult RestoreResult(PhantomManorRoundResult result, PhantomManorPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, PhantomManorPreparedRound round, PhantomManorRoundResult result,
            DateTime completed) => PhantomManorSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, PhantomManorRoundRequest? pending = null) =>
            new PhantomManorRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new PhantomManorRoundCooldownException(remaining);

        protected override Exception Paused() => new PhantomManorRoundPausedException();
    }
}
