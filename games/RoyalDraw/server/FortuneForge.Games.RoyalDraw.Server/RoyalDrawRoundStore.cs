using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.RoyalDraw.Server;

public sealed class RoyalDrawRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "royalDrawPreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<RoyalDrawRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return RoyalDrawRoundStateReply.From(before, pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<RoyalDrawRoundResult> SpinAsync(string account, RoyalDrawRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<RoyalDrawRoundRequest, RoyalDrawPreparedRound, RoyalDrawRoundResult>(
            database, clock, RoyalDrawRoundStateCodec.Storage, new(RoyalDrawEngine.GameId, RoyalDrawPaidRouteContract.AuditGameId,
                RoyalDrawEngine.GameId, "Royal Draw", RawCollectionName, RoyalDrawRoundStateCodec.OwnerField))
    {
        protected override RoyalDrawPreparedRound PrepareRound(string account, RoyalDrawRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            RoyalDrawRoundPolicy.Prepare(account, request, before, now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(RoyalDrawPreparedRound round) => RoyalDrawRoundPolicy.Validate(round);

        protected override RoyalDrawPreparedRound RemoveObservation(RoyalDrawPreparedRound round) => round with { Observation = null };

        protected override RoyalDrawRoundResult CreateResult(RoyalDrawPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? RoyalDrawRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override RoyalDrawRoundResult RestoreResult(RoyalDrawRoundResult result, RoyalDrawPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, RoyalDrawPreparedRound round, RoyalDrawRoundResult result,
            DateTime completed) => RoyalDrawSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, RoyalDrawRoundRequest? pending = null) =>
            new RoyalDrawRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new RoyalDrawRoundCooldownException(remaining);

        protected override Exception Paused() => new RoyalDrawRoundPausedException();
    }
}
