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

    internal async Task<CosmicFortuneRoundStateReply> ActAsync(string account, string operationId, SealedTombAction action,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token)
    {
        if (!CosmicFortuneRoundRequest.IsValidOperationId(operationId))
            throw new ArgumentException("Include one valid Sealed Tomb action operation ID.");
        var balance = await accounts.GetStateAsync(account, token);
        var (_, pending) = await persistence.ReadStateAsync(account, balance.LegacyFreeGamesRemaining, token);
        CosmicFortuneRoundRequest request;
        if (pending is not null)
        {
            if (pending.TombAction != action)
                throw new SealedTombConflictException("tomb-operation-pending", "Retry the pending tomb action before choosing another room.");
            request = pending;
        }
        else request = new(operationId, 0, false) { TombAction = action };
        var result = await persistence.SpinAsync(account, request, true, wallet, accounts, token);
        return result.Features ?? throw new GameWalletUnavailableException();
    }

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<CosmicFortuneRoundRequest, CosmicFortunePreparedRound, CosmicFortuneRoundResult>(
            database, clock, CosmicFortuneRoundStateCodec.Storage, new(CosmicFortuneEngine.GameId, CosmicFortunePaidRouteContract.AuditGameId,
                CosmicFortuneEngine.GameId, "Pharaoh's Ascent", RawCollectionName, CosmicFortuneRoundStateCodec.OwnerField))
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
            DateTime completed)
        {
            if (round.TombEvidence is not null) SealedTombEvidence.Write(transaction, database, round, result.Wallet, completed);
            else CosmicFortuneSpinReport.Write(transaction, database, round, result, completed);
        }

        protected override Exception Conflict(string message, CosmicFortuneRoundRequest? pending = null) =>
            new CosmicFortuneRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new CosmicFortuneRoundCooldownException(remaining);

        protected override Exception Paused() => new CosmicFortuneRoundPausedException();
    }
}
