using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.DesertTreasures.Server;

public sealed class DesertTreasuresRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "desertTreasuresPreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<DesertTreasuresRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return DesertTreasuresRoundStateReply.From(before, pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<DesertTreasuresRoundResult> SpinAsync(string account, DesertTreasuresRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<DesertTreasuresRoundRequest, DesertTreasuresPreparedRound, DesertTreasuresRoundResult>(
            database, clock, DesertTreasuresRoundStateCodec.Storage, new(DesertTreasuresEngine.GameId, DesertTreasuresPaidRouteContract.AuditGameId,
                DesertTreasuresEngine.GameId, "Desert Treasures", RawCollectionName, DesertTreasuresRoundStateCodec.OwnerField))
    {
        protected override DesertTreasuresPreparedRound PrepareRound(string account, DesertTreasuresRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            DesertTreasuresRoundPolicy.Prepare(account, request, before, now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(DesertTreasuresPreparedRound round) => DesertTreasuresRoundPolicy.Validate(round);

        protected override DesertTreasuresPreparedRound RemoveObservation(DesertTreasuresPreparedRound round) => round with { Observation = null };

        protected override DesertTreasuresRoundResult CreateResult(DesertTreasuresPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? DesertTreasuresRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override DesertTreasuresRoundResult RestoreResult(DesertTreasuresRoundResult result, DesertTreasuresPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, DesertTreasuresPreparedRound round, DesertTreasuresRoundResult result,
            DateTime completed) => DesertTreasuresSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, DesertTreasuresRoundRequest? pending = null) =>
            new DesertTreasuresRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new DesertTreasuresRoundCooldownException(remaining);

        protected override Exception Paused() => new DesertTreasuresRoundPausedException();
    }
}
