using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.SamuraiFortune.Server;

public sealed class SamuraiFortuneRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "samuraiFortunePreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<SamuraiFortuneRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return SamuraiFortuneRoundStateReply.From(before, pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<SamuraiFortuneRoundResult> SpinAsync(string account, SamuraiFortuneRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<SamuraiFortuneRoundRequest, SamuraiFortunePreparedRound, SamuraiFortuneRoundResult>(
            database, clock, SamuraiFortuneRoundStateCodec.Storage, new(SamuraiFortuneEngine.GameId, SamuraiFortunePaidRouteContract.AuditGameId,
                SamuraiFortuneEngine.GameId, "Samurai Fortune", RawCollectionName, SamuraiFortuneRoundStateCodec.OwnerField))
    {
        protected override SamuraiFortunePreparedRound PrepareRound(string account, SamuraiFortuneRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            SamuraiFortuneRoundPolicy.Prepare(account, request, before, now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(SamuraiFortunePreparedRound round) => SamuraiFortuneRoundPolicy.Validate(round);

        protected override SamuraiFortunePreparedRound RemoveObservation(SamuraiFortunePreparedRound round) => round with { Observation = null };

        protected override SamuraiFortuneRoundResult CreateResult(SamuraiFortunePreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? SamuraiFortuneRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override SamuraiFortuneRoundResult RestoreResult(SamuraiFortuneRoundResult result, SamuraiFortunePreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, SamuraiFortunePreparedRound round, SamuraiFortuneRoundResult result,
            DateTime completed) => SamuraiFortuneSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, SamuraiFortuneRoundRequest? pending = null) =>
            new SamuraiFortuneRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new SamuraiFortuneRoundCooldownException(remaining);

        protected override Exception Paused() => new SamuraiFortuneRoundPausedException();
    }
}
