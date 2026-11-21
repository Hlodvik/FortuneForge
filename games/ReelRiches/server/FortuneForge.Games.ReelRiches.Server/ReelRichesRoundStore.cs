using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.ReelRiches.Server;

public sealed class ReelRichesRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "reelRichesPreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<ReelRichesRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return ReelRichesRoundStateReply.From(before, pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<ReelRichesRoundResult> SpinAsync(string account, ReelRichesRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<ReelRichesRoundRequest, ReelRichesPreparedRound, ReelRichesRoundResult>(
            database, clock, ReelRichesRoundStateCodec.Storage, new(ReelRichesEngine.GameId, ReelRichesPaidRouteContract.AuditGameId,
                ReelRichesEngine.GameId, "Reel Riches", RawCollectionName, ReelRichesRoundStateCodec.OwnerField))
    {
        protected override ReelRichesPreparedRound PrepareRound(string account, ReelRichesRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            ReelRichesRoundPolicy.Prepare(account, request, before, now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(ReelRichesPreparedRound round) => ReelRichesRoundPolicy.Validate(round);

        protected override ReelRichesPreparedRound RemoveObservation(ReelRichesPreparedRound round) => round with { Observation = null };

        protected override ReelRichesRoundResult CreateResult(ReelRichesPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? ReelRichesRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override ReelRichesRoundResult RestoreResult(ReelRichesRoundResult result, ReelRichesPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, ReelRichesPreparedRound round, ReelRichesRoundResult result,
            DateTime completed) => ReelRichesSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, ReelRichesRoundRequest? pending = null) =>
            new ReelRichesRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new ReelRichesRoundCooldownException(remaining);

        protected override Exception Paused() => new ReelRichesRoundPausedException();
    }
}
