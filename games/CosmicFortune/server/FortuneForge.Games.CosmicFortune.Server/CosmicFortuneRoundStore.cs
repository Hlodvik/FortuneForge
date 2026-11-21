using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.CosmicFortune.Server;

public sealed class CosmicFortuneRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "cosmicFortunePreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<CosmicFortuneRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return CosmicFortuneRoundStateReply.From(before, pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<CosmicFortuneRoundResult> SpinAsync(string account, CosmicFortuneRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<CosmicFortuneRoundRequest, CosmicFortunePreparedRound, CosmicFortuneRoundResult>(
            database, clock, CosmicFortuneRoundStateCodec.Storage, new(CosmicFortuneEngine.GameId, CosmicFortunePaidRouteContract.AuditGameId,
                CosmicFortuneEngine.GameId, "Cosmic Fortune", RawCollectionName, CosmicFortuneRoundStateCodec.OwnerField))
    {
        protected override CosmicFortunePreparedRound PrepareRound(string account, CosmicFortuneRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            CosmicFortuneRoundPolicy.Prepare(account, request, before, now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(CosmicFortunePreparedRound round) => CosmicFortuneRoundPolicy.Validate(round);

        protected override CosmicFortunePreparedRound RemoveObservation(CosmicFortunePreparedRound round) => round with { Observation = null };

        protected override CosmicFortuneRoundResult CreateResult(CosmicFortunePreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? CosmicFortuneRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override CosmicFortuneRoundResult RestoreResult(CosmicFortuneRoundResult result, CosmicFortunePreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, CosmicFortunePreparedRound round, CosmicFortuneRoundResult result,
            DateTime completed) => CosmicFortuneSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, CosmicFortuneRoundRequest? pending = null) =>
            new CosmicFortuneRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new CosmicFortuneRoundCooldownException(remaining);

        protected override Exception Paused() => new CosmicFortuneRoundPausedException();
    }
}
