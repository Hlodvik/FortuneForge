using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.HighNoonFortune.Server;

public sealed class HighNoonRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "highNoonFortunePreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<HighNoonRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return HighNoonRoundStateReply.From(before, pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<HighNoonRoundResult> SpinAsync(string account, HighNoonRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<HighNoonRoundRequest, HighNoonPreparedRound, HighNoonRoundResult>(
            database, clock, HighNoonRoundStateCodec.Storage, new(HighNoonFortuneEngine.GameId, HighNoonPaidRouteContract.AuditGameId,
                HighNoonFortuneEngine.GameId, "High Noon Fortune", RawCollectionName, HighNoonRoundStateCodec.OwnerField))
    {
        protected override HighNoonPreparedRound PrepareRound(string account, HighNoonRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            HighNoonRoundPolicy.Prepare(account, request, before, now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(HighNoonPreparedRound round) => HighNoonRoundPolicy.Validate(round);

        protected override HighNoonPreparedRound RemoveObservation(HighNoonPreparedRound round) => round with { Observation = null };

        protected override HighNoonRoundResult CreateResult(HighNoonPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? HighNoonRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override HighNoonRoundResult RestoreResult(HighNoonRoundResult result, HighNoonPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, HighNoonPreparedRound round, HighNoonRoundResult result,
            DateTime completed) => HighNoonSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, HighNoonRoundRequest? pending = null) =>
            new HighNoonRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new HighNoonRoundCooldownException(remaining);

        protected override Exception Paused() => new HighNoonRoundPausedException();
    }
}
