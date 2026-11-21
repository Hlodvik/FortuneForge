using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.Wukong.Server;

public sealed class WukongRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "wukongPreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<WukongRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return WukongRoundStateReply.From(WukongRoundBefore.FromShared(before), pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<WukongRoundResult> SpinAsync(string account, WukongRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<WukongRoundRequest, WukongPreparedRound, WukongRoundResult>(
            database, clock, WukongRoundStateCodec.Storage, new(WukongEngine.GameId, WukongPaidRouteContract.AuditGameId,
                WukongEngine.LegacyStorageGameId, "Wukong", RawCollectionName, WukongRoundStateCodec.OwnerField))
    {
        protected override WukongPreparedRound PrepareRound(string account, WukongRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            WukongRoundPolicy.Prepare(account, request, WukongRoundBefore.FromShared(before), now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(WukongPreparedRound round) => WukongRoundPolicy.Validate(round);

        protected override WukongPreparedRound RemoveObservation(WukongPreparedRound round) => round with { Observation = null };

        protected override WukongRoundResult CreateResult(WukongPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? WukongRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override WukongRoundResult RestoreResult(WukongRoundResult result, WukongPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, WukongPreparedRound round, WukongRoundResult result,
            DateTime completed) => WukongSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, WukongRoundRequest? pending = null) =>
            new WukongRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new WukongRoundCooldownException(remaining);

        protected override Exception Paused() => new WukongRoundPausedException();
    }
}
