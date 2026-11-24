using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.PharaohsAscent.Server;

public sealed class PharaohsAscentRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "pharaohsAscentPreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<PharaohsAscentRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return PharaohsAscentRoundStateReply.From(before, pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<PharaohsAscentRoundResult> SpinAsync(string account, PharaohsAscentRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    internal async Task<PharaohsAscentRoundStateReply> ActAsync(string account, string operationId, SealedTombAction action,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token)
    {
        if (!PharaohsAscentRoundRequest.IsValidOperationId(operationId))
            throw new ArgumentException("Include one valid Sealed Tomb action operation ID.");
        var balance = await accounts.GetStateAsync(account, token);
        var (_, pending) = await persistence.ReadStateAsync(account, balance.LegacyFreeGamesRemaining, token);
        PharaohsAscentRoundRequest request;
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
        : ClassicSlotPaidRoundStore<PharaohsAscentRoundRequest, PharaohsAscentPreparedRound, PharaohsAscentRoundResult>(
            database, clock, PharaohsAscentRoundStateCodec.Storage, new(PharaohsAscentEngine.GameId, PharaohsAscentPaidRouteContract.AuditGameId,
                PharaohsAscentEngine.GameId, "Pharaoh's Ascent", RawCollectionName, PharaohsAscentRoundStateCodec.OwnerField))
    {
        protected override PharaohsAscentPreparedRound PrepareRound(string account, PharaohsAscentRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            PharaohsAscentRoundPolicy.Prepare(account, request, before, now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(PharaohsAscentPreparedRound round) => PharaohsAscentRoundPolicy.Validate(round);

        protected override PharaohsAscentPreparedRound RemoveObservation(PharaohsAscentPreparedRound round) => round with { Observation = null };

        protected override PharaohsAscentRoundResult CreateResult(PharaohsAscentPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? PharaohsAscentRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override PharaohsAscentRoundResult RestoreResult(PharaohsAscentRoundResult result, PharaohsAscentPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, PharaohsAscentPreparedRound round, PharaohsAscentRoundResult result,
            DateTime completed)
        {
            if (round.TombEvidence is not null) SealedTombEvidence.Write(transaction, database, round, result.Wallet, completed);
            else PharaohsAscentSpinReport.Write(transaction, database, round, result, completed);
        }

        protected override Exception Conflict(string message, PharaohsAscentRoundRequest? pending = null) =>
            new PharaohsAscentRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new PharaohsAscentRoundCooldownException(remaining);

        protected override Exception Paused() => new PharaohsAscentRoundPausedException();
    }
}
