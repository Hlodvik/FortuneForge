using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Cloud.Firestore;

namespace FortuneForge.Games.GodsOfOlympus.Server;

public sealed class OlympusRoundStore(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
{
    public const string RawCollectionName = "godsOfOlympusPreparedRounds";
    private readonly Persistence persistence = new(database, random, clock);

    internal async Task<OlympusRoundStateReply> ReadStateAsync(string account, GameWalletAccountState wallet, CancellationToken token)
    {
        var (before, pendingRequest) = await persistence.ReadStateAsync(account, wallet.LegacyFreeGamesRemaining, token);
        return OlympusRoundStateReply.From(before, pendingRequest, wallet.SlotsCreditsBalanceCents);
    }

    internal Task<OlympusRoundResult> SpinAsync(string account, OlympusRoundRequest request, bool allowStart,
        IGameWalletClient wallet, IGameWalletAccountReader accounts, CancellationToken token) =>
        persistence.SpinAsync(account, request, allowStart, wallet, accounts, token);

    private sealed class Persistence(FirestoreDb database, IGameRandomSource random, TimeProvider clock)
        : ClassicSlotPaidRoundStore<OlympusRoundRequest, OlympusPreparedRound, OlympusRoundResult>(
            database, clock, OlympusRoundStateCodec.Storage, new(GodsOfOlympusEngine.GameId, OlympusPaidRouteContract.AuditGameId,
                GodsOfOlympusEngine.GameId, "Gods of Olympus", RawCollectionName, OlympusRoundStateCodec.OwnerField))
    {
        protected override OlympusPreparedRound PrepareRound(string account, OlympusRoundRequest request,
            ClassicSlotRoundState before, DateTime now, GameRequestObservation? observation, bool importing) =>
            OlympusRoundPolicy.Prepare(account, request, before, now, observation, random)
                with { ImportedLegacyState = importing };

        protected override void ValidateRound(OlympusPreparedRound round) => OlympusRoundPolicy.Validate(round);

        protected override OlympusPreparedRound RemoveObservation(OlympusPreparedRound round) => round with { Observation = null };

        protected override OlympusRoundResult CreateResult(OlympusPreparedRound round, GameWalletSettlementReceipt receipt, bool settled) =>
            new(settled ? OlympusRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents) : null, receipt)
            {
                Round = settled ? round : null,
                ChargedWagerCents = round.WagerCents
            };

        protected override OlympusRoundResult RestoreResult(OlympusRoundResult result, OlympusPreparedRound? round) =>
            result with { Round = round };

        protected override void Report(Transaction transaction, FirestoreDb database, OlympusPreparedRound round, OlympusRoundResult result,
            DateTime completed) => OlympusSpinReport.Write(transaction, database, round, result, completed);

        protected override Exception Conflict(string message, OlympusRoundRequest? pending = null) =>
            new OlympusRoundConflictException(message, pending);

        protected override Exception CoolingDown(TimeSpan remaining) => new OlympusRoundCooldownException(remaining);

        protected override Exception Paused() => new OlympusRoundPausedException();
    }
}
