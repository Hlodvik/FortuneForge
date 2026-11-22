using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.RobotRevolution.Server;

public sealed class RobotRevolutionRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "robotRevolutionPreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<RobotRevolutionRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return RobotRevolutionRoundStateReply.From(before, pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<RobotRevolutionRoundResult> SpinAsync(string account, RobotRevolutionRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<RobotRevolutionRoundRequest, RobotRevolutionPreparedRound, RobotRevolutionRoundResult>(
            database, clock, RobotRevolutionRoundStateCodec.Storage, new(RobotRevolutionEngine.GameId, RobotRevolutionPaidRouteContract.AuditGameId,
                RobotRevolutionEngine.GameId, "Sunstone Eclipse", RawCollectionName, RobotRevolutionRoundStateCodec.OwnerField))
    {
        protected override RobotRevolutionPreparedRound PrepareRound(string account, RobotRevolutionRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            RobotRevolutionRoundPolicy.Prepare(account, request, before, now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(RobotRevolutionPreparedRound round) => RobotRevolutionRoundPolicy.Validate(round);

        protected override RobotRevolutionPreparedRound RemoveObservation(RobotRevolutionPreparedRound round) => round with { Observation = null };

        protected override RobotRevolutionRoundResult CreateResult(RobotRevolutionPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? RobotRevolutionRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override RobotRevolutionRoundResult RestoreResult(RobotRevolutionRoundResult result, RobotRevolutionPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, RobotRevolutionPreparedRound round, RobotRevolutionRoundResult result,
            DateTime completed) => RobotRevolutionSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, RobotRevolutionRoundRequest? pending = null) =>
            new RobotRevolutionRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new RobotRevolutionRoundCooldownException(remaining);

        protected override Exception Paused() => new RobotRevolutionRoundPausedException();
    }
}
