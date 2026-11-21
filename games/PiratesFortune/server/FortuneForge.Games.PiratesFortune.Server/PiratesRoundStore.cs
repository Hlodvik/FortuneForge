using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.PiratesFortune.Server;

public sealed class PiratesRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "piratesFortunePreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<PiratesRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return PiratesRoundStateReply.From(PiratesRoundBefore.FromShared(before), pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<PiratesRoundResult> SpinAsync(string account, PiratesRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<PiratesRoundRequest, PiratesPreparedRound, PiratesRoundResult>(
            database, clock, PiratesRoundStateCodec.Storage, new(PiratesFortuneEngine.GameId, PiratesPaidRouteContract.AuditGameId,
                PiratesFortuneEngine.GameId, "Pirates' Fortune", RawCollectionName, PiratesRoundStateCodec.OwnerField))
    {
        protected override PiratesPreparedRound PrepareRound(string account, PiratesRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            PiratesRoundPolicy.Prepare(account, request, PiratesRoundBefore.FromShared(before), now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(PiratesPreparedRound round) => PiratesRoundPolicy.Validate(round);

        protected override PiratesPreparedRound RemoveObservation(PiratesPreparedRound round) => round with { Observation = null };

        protected override PiratesRoundResult CreateResult(PiratesPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? PiratesRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override PiratesRoundResult RestoreResult(PiratesRoundResult result, PiratesPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, PiratesPreparedRound round, PiratesRoundResult result,
            DateTime completed) => PiratesSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, PiratesRoundRequest? pending = null) =>
            new PiratesRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new PiratesRoundCooldownException(remaining);

        protected override Exception Paused() => new PiratesRoundPausedException();
    }
}
