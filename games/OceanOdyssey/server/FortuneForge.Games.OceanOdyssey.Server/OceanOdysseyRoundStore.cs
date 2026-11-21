using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.OceanOdyssey.Server;

public sealed class OceanOdysseyRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "oceanOdysseyPreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<OceanOdysseyRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return OceanOdysseyRoundStateReply.From(before, pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<OceanOdysseyRoundResult> SpinAsync(string account, OceanOdysseyRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<OceanOdysseyRoundRequest, OceanOdysseyPreparedRound, OceanOdysseyRoundResult>(
            database, clock, OceanOdysseyRoundStateCodec.Storage, new(OceanOdysseyEngine.GameId, OceanOdysseyPaidRouteContract.AuditGameId,
                OceanOdysseyEngine.GameId, "Ocean Odyssey", RawCollectionName, OceanOdysseyRoundStateCodec.OwnerField))
    {
        protected override OceanOdysseyPreparedRound PrepareRound(string account, OceanOdysseyRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            OceanOdysseyRoundPolicy.Prepare(account, request, before, now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(OceanOdysseyPreparedRound round) => OceanOdysseyRoundPolicy.Validate(round);

        protected override OceanOdysseyPreparedRound RemoveObservation(OceanOdysseyPreparedRound round) => round with { Observation = null };

        protected override OceanOdysseyRoundResult CreateResult(OceanOdysseyPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? OceanOdysseyRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override OceanOdysseyRoundResult RestoreResult(OceanOdysseyRoundResult result, OceanOdysseyPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, OceanOdysseyPreparedRound round, OceanOdysseyRoundResult result,
            DateTime completed) => OceanOdysseySpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, OceanOdysseyRoundRequest? pending = null) =>
            new OceanOdysseyRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new OceanOdysseyRoundCooldownException(remaining);

        protected override Exception Paused() => new OceanOdysseyRoundPausedException();
    }
}
