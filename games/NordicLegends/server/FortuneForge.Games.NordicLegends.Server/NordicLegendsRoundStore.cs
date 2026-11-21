using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.NordicLegends.Server;

public sealed class NordicLegendsRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "nordicLegendsPreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<NordicLegendsRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return NordicLegendsRoundStateReply.From(before, pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<NordicLegendsRoundResult> SpinAsync(string account, NordicLegendsRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<NordicLegendsRoundRequest, NordicLegendsPreparedRound, NordicLegendsRoundResult>(
            database, clock, NordicLegendsRoundStateCodec.Storage, new(NordicLegendsEngine.GameId, NordicLegendsPaidRouteContract.AuditGameId,
                NordicLegendsEngine.GameId, "Nordic Legends", RawCollectionName, NordicLegendsRoundStateCodec.OwnerField))
    {
        protected override NordicLegendsPreparedRound PrepareRound(string account, NordicLegendsRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            NordicLegendsRoundPolicy.Prepare(account, request, before, now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(NordicLegendsPreparedRound round) => NordicLegendsRoundPolicy.Validate(round);

        protected override NordicLegendsPreparedRound RemoveObservation(NordicLegendsPreparedRound round) => round with { Observation = null };

        protected override NordicLegendsRoundResult CreateResult(NordicLegendsPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? NordicLegendsRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override NordicLegendsRoundResult RestoreResult(NordicLegendsRoundResult result, NordicLegendsPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, NordicLegendsPreparedRound round, NordicLegendsRoundResult result,
            DateTime completed) => NordicLegendsSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, NordicLegendsRoundRequest? pending = null) =>
            new NordicLegendsRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new NordicLegendsRoundCooldownException(remaining);

        protected override Exception Paused() => new NordicLegendsRoundPausedException();
    }
}
