using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.SamuraiFortune.Server;
using FortuneForge.Games.Slots;
using FortuneForge.Games.Slots.Persistence;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.SamuraiFortune.Tests;

internal static class SamuraiTestSupport
{
    internal const string Account = "samurai-native-player";
    internal static FirestoreDb Connect() => new FirestoreDbBuilder
    {
        ProjectId = $"samurai-{Guid.NewGuid():N}"[..29],
        EmulatorDetection = EmulatorDetection.EmulatorOnly
    }.Build();

    internal static DocumentReference Guard(FirestoreDb database, string? account = null) =>
        database.Collection(SlotSpinGuard.Collection).Document(SlotSpinGuard.DocumentId(account ?? Account, SamuraiFortuneEngine.GameId));

    internal static SamuraiFortuneRoundService Service(FirestoreDb database, Wallet wallet, IGameRandomSource? random = null,
        TimeProvider? clock = null) => new(new SamuraiFortuneRoundStore(database, random ?? new HighDraws(), clock ?? TimeProvider.System), wallet, wallet);

    internal static Task SeedBalance(FirestoreDb database, string name, long value)
    {
        var currency = $"{name}:{SamuraiFortuneEngine.GameId}";
        return database.Collection("userBalances").Document($"{Account}_{currency}").CreateAsync(
            new Dictionary<string, object> { ["userId"] = Account, ["currencyId"] = currency, ["available"] = value });
    }

    internal static ClassicSlotRoundState Empty(long energy = 0) =>
        new(0, 0, null, 0, energy, 0, new Dictionary<string, long>(), new Dictionary<string, long>());

    internal static SamuraiFortuneRoundResult Settle(SamuraiFortunePreparedRound round)
    {
        const long before = 100_000;
        var afterWager = before - round.WagerCents;
        var spin = round.Outcome.SpinId.ToString("N");
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(SamuraiFortuneEngine.GameId, round.AccountId, spin),
            round.WagerCents > 0 ? $"{spin}-wager" : null, round.PayoutCents > 0 ? $"{spin}-payout" : null,
            before, afterWager, afterWager + round.PayoutCents, DateTimeOffset.UtcNow, false, false,
            LegacyFreeGameTransactionId: round.SpendsLegacy ? $"{spin}-free-game-use" : null,
            LegacyFreeGamesRemaining: round.Progress.After.LegacyRemaining);
        return new(SamuraiFortuneRoundStateReply.From(round.Progress.After, balanceCents: receipt.BalanceAfterCents), receipt)
        { Round = round, ChargedWagerCents = round.WagerCents };
    }

    internal sealed class HighDraws : IGameRandomSource
    {
        public int Next(int maximumExclusive) => maximumExclusive - 1;
    }

    internal sealed class NoDraws : IGameRandomSource
    {
        public int Next(int maximumExclusive) => throw new InvalidOperationException("Recovery cannot draw another outcome.");
    }

    internal sealed class SeededDraws(int seed) : IGameRandomSource
    {
        private readonly Random random = new(seed);
        public int Next(int maximumExclusive) => random.Next(maximumExclusive);
    }

    internal sealed class RoundClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow.AddSeconds(-10);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance() => now = now.AddSeconds(1);
    }

    internal sealed class Wallet : IGameWalletClient, IGameWalletAccountReader
    {
        private readonly Dictionary<string, (GameWalletSettlementRequest Request, GameWalletSettlementReceipt Receipt)> receipts = [];
        public long Balance { get; private set; } = 100_000;
        public long LegacyRemaining { get; set; }
        public long? ChangeBeforeSettle { get; init; }
        public bool LoseNextReply { get; set; }
        public bool DeclineFunds { get; init; }
        public int Calls { get; private set; }
        public int Approvals { get; private set; }
        public GameWalletSettlementRequest? Last { get; private set; }

        public Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken) =>
            Task.FromResult(new GameWalletAccountState(LegacyRemaining, Balance));

        public Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Last = request;
            if (receipts.TryGetValue(request.OperationId, out var saved))
            {
                Assert.Equal(saved.Request, request);
                return Task.FromResult(saved.Receipt with { WasAlreadySettled = true });
            }
            if (ChangeBeforeSettle is { } changed) LegacyRemaining = changed;
            var mismatch = request.ExpectedLegacyFreeGamesRemaining != LegacyRemaining;
            var accepted = !mismatch && !DeclineFunds;
            var before = Balance;
            if (accepted)
            {
                Balance = checked(Balance - request.WagerCents + request.PayoutCents);
                if (request.SpendLegacyFreeGame) LegacyRemaining--;
                Approvals++;
            }
            var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(SamuraiFortuneEngine.GameId, accountId, request.OperationId),
                accepted && request.WagerCents > 0 ? $"{request.OperationId}-wager" : null,
                accepted && request.PayoutCents > 0 ? $"{request.OperationId}-payout" : null,
                before, accepted ? before - request.WagerCents : before, Balance, DateTimeOffset.UtcNow,
                false, DeclineFunds, false,
                accepted && request.SpendLegacyFreeGame ? $"{request.OperationId}-free-game-use" : null, LegacyRemaining, mismatch);
            receipts.Add(request.OperationId, (request, receipt));
            if (LoseNextReply)
            {
                LoseNextReply = false;
                throw new GameWalletUnavailableException();
            }
            return Task.FromResult(receipt);
        }
    }
}

public sealed class SamuraiEmulatorFactAttribute : FactAttribute
{
    public SamuraiEmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            Skip = "A Firestore emulator is required.";
    }
}
