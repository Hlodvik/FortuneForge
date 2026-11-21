using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.RainbowRealm.Server;

public sealed class RainbowRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "rainbowRealmPreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<RainbowRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return RainbowRoundStateReply.From(RainbowRoundBefore.FromShared(before), pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<RainbowRoundResult> SpinAsync(string account, RainbowRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<RainbowRoundRequest, RainbowPreparedRound, RainbowRoundResult>(
            database, clock, RainbowRoundStateCodec.Storage, new(RainbowRealmEngine.GameId, RainbowPaidRouteContract.AuditGameId,
                RainbowRealmEngine.GameId, "Rainbow Realm", RawCollectionName, RainbowRoundStateCodec.OwnerField))
    {
        protected override RainbowPreparedRound PrepareRound(string account, RainbowRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            RainbowRoundPolicy.Prepare(account, request, RainbowRoundBefore.FromShared(before), now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(RainbowPreparedRound round) => RainbowRoundPolicy.Validate(round);

        protected override RainbowPreparedRound RemoveObservation(RainbowPreparedRound round) => round with { Observation = null };

        protected override RainbowRoundResult CreateResult(RainbowPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? RainbowRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override RainbowRoundResult RestoreResult(RainbowRoundResult result, RainbowPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, RainbowPreparedRound round, RainbowRoundResult result,
            DateTime completed) => RainbowSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, RainbowRoundRequest? pending = null) =>
            new RainbowRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new RainbowRoundCooldownException(remaining);

        protected override Exception Paused() => new RainbowRoundPausedException();
    }
}
