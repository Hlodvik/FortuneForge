using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.CrookedRoad.Server;
using FortuneForge.Games.CrookedRoad.Tests.Support;
using FortuneForge.Games.Slots;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace FortuneForge.Games.CrookedRoad.Tests;

public sealed class CrookedRoadPaidLifecycleTests
{
    private static bool HasLocalEmulator => Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST") is { } host &&
        Uri.TryCreate("http://" + host, UriKind.Absolute, out var uri) && uri.IsLoopback;
    private sealed class FirestoreFactAttribute : FactAttribute
    {
        public FirestoreFactAttribute()
        {
            if (!HasLocalEmulator)
                Skip = "A loopback Firestore emulator is required; production Firestore is forbidden.";
        }
    }
    private sealed class FirestoreTheoryAttribute : TheoryAttribute
    {
        public FirestoreTheoryAttribute()
        {
            if (!HasLocalEmulator) Skip = "A loopback Firestore emulator is required; production Firestore is forbidden.";
        }
    }
    private sealed class TestTime : TimeProvider
    {
        private long ticks = DateTimeOffset.UtcNow.UtcTicks;
        public void Advance() => Interlocked.Add(ref ticks, TimeSpan.TicksPerSecond);
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    }
    private sealed class HighRandom : IGameRandomSource { public int Next(int maximumExclusive) => maximumExclusive - 1; }
    private static (CrookedRoadRoundService Service, CrookedRoadTestWallet Wallet, FirestoreDb Database, TestTime Clock) Context(long balance = 1_000_000)
    {
        if (!HasLocalEmulator) throw new InvalidOperationException("Only a loopback test emulator is permitted.");
        var database = new FirestoreDbBuilder { ProjectId = "crooked-road-tests-" + Guid.NewGuid().ToString("N"), EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        var wallet = new CrookedRoadTestWallet(balance);
        var clock = new TestTime();
        var rounds = new CrookedRoadRoundStore(database, new HighRandom(), clock);
        var actions = new CrookedRoadActionStore(database, new HighRandom(), clock);
        return (new(rounds, actions, wallet, wallet), wallet, database, clock);
    }

    [FirestoreFact]
    public async Task ConcurrentExactRetriesFreezeOneOutcomeAndChargeOneWalletEntry()
    {
        var (service, wallet, _, _) = Context();
        var request = new CrookedRoadRoundRequest(Guid.NewGuid().ToString(), 100, false);
        var replies = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.SpinAsync("account", request, true, default)));
        Assert.Single(replies.Select(reply => reply.Round!.SpinId).Distinct());
        Assert.Equal(1, wallet.UniqueSettlements);
        Assert.Null(wallet.LastRequest!.ExpectedLegacyFreeGamesRemaining);
        var state = await service.ReadStateAsync("account", default);
        Assert.Equal(1, state.GameFeatureState.Funding.Spins);
        Assert.Equal(9999m, state.SlotsCreditsBalance);
        Assert.Null(state.PendingOperation);
        await Assert.ThrowsAsync<CrookedRoadPendingException>(() => service.SpinAsync("account", request with { WagerPoints = 200 }, true, default));
    }

    [FirestoreFact]
    public async Task LostWalletReplyBlocksMapUntilOriginalOperationRecoversWithoutAnotherCharge()
    {
        var (service, wallet, _, _) = Context();
        wallet.FailAfterNextSettlement = true;
        var request = new CrookedRoadRoundRequest(Guid.NewGuid().ToString(), 100, false);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => service.SpinAsync("account", request, true, default));
        var pending = await service.ReadStateAsync("account", default);
        Assert.Equal(request, pending.PendingOperation);
        await Assert.ThrowsAsync<CrookedRoadConflictException>(() => service.ActAsync("account", Guid.NewGuid().ToString(),
            new(pending.GameFeatureState.Revision, "place", 0, 7, 1), default));
        var recovered = await service.SpinAsync("account", request, false, default);
        Assert.True(recovered.Settled);
        Assert.Equal(1, wallet.UniqueSettlements);
        Assert.Equal(9999m, recovered.Features!.SlotsCreditsBalance);
        var state = await service.ReadStateAsync("account", default);
        var action = new CrookedRoadAction(state.GameFeatureState.Revision, "place", 0, 7, 1);
        var operation = Guid.NewGuid().ToString();
        var first = await service.ActAsync("account", operation, action, default);
        var retry = await service.ActAsync("account", operation, action, default);
        Assert.Equal(first.GameFeatureState.Revision, retry.GameFeatureState.Revision);
        Assert.Equal(first.SlotsCreditsBalance, retry.SlotsCreditsBalance);
        await Assert.ThrowsAsync<CrookedRoadConflictException>(() => service.ActAsync("account", Guid.NewGuid().ToString(), action, default));
    }

    [FirestoreFact]
    public async Task DeclinedWagerDoesNotAdvanceFundingOrEraseRoadState()
    {
        var (service, wallet, _, _) = Context(0);
        var result = await service.SpinAsync("account", new(Guid.NewGuid().ToString(), 100, false), true, default);
        Assert.False(result.Settled);
        Assert.True(result.Wallet.InsufficientFunds);
        var state = await service.ReadStateAsync("account", default);
        Assert.Equal(0, state.GameFeatureState.Funding.Spins);
        Assert.Equal(2, state.GameFeatureState.Hand.Length);
        Assert.Null(state.PendingOperation);
        Assert.Equal(0m, state.SlotsCreditsBalance);
    }

    [FirestoreTheory]
    [InlineData("well")]
    [InlineData("fort")]
    [InlineData("hill")]
    public async Task TwentyPaidWagersFundOneRealRoadJourneyWhoseFeatureStepsNeverCharge(string destination)
    {
        var (service, wallet, _, clock) = Context();
        for (var index = 0; index < 20; index++)
        {
            clock.Advance();
            var paid = await service.SpinAsync("account", new(Guid.NewGuid().ToString(), 100, false), true, default);
            Assert.Equal(100, paid.ChargedWagerCents);
        }
        var state = await service.ReadStateAsync("account", default);
        Assert.Single(state.GameFeatureState.Tickets);
        async Task Act(string action, int? hand = null, int? cell = null, int? rotation = null, string? exit = null, int? choice = null)
        {
            state = await service.ActAsync("account", Guid.NewGuid().ToString(),
                new(state.GameFeatureState.Revision, action, hand, cell, rotation, exit, choice), default);
        }
        await Act("place", 0, 7, 1);
        await Act("place", 0, 0, 0); // Park the starting corner; remaining earned pieces are junctions.
        await Act("place", 0, 4, destination == "well" ? 2 : 1);
        await Act("place", 0, destination == "well" ? 3 : destination == "fort" ? 1 : 5, destination == "fort" ? 1 : 0);
        await Act("set-off", exit: destination);
        Assert.Equal(destination, state.FreeSpinFeatureMode);
        Assert.Empty(state.GameFeatureState.Tickets);
        long featureAwards = 0;
        while (state.GameFeatureState.ActiveBonus is not null)
        {
            if (state.FeatureActionRequired)
                await Act(destination == "well" ? "choose-spring" : "choose-door", choice: 0);
            clock.Advance();
            var play = await service.SpinAsync("account", new(Guid.NewGuid().ToString(), 100, true), false, default);
            Assert.Equal(0, play.ChargedWagerCents);
            Assert.Null(wallet.LastRequest!.ExpectedLegacyFreeGamesRemaining);
            featureAwards += play.Round!.Outcome.Payout.TotalPoints;
            state = play.Features!;
        }
        Assert.Equal(1, state.GameFeatureState.CompletedJourneys);
        Assert.Equal(0, state.GameFeatureState.Funding.Spins);
        Assert.Equal((1_000_000 - 2000 + featureAwards) / 100m, state.SlotsCreditsBalance);
        Assert.Equal(0, state.FreeSpinsRemaining);
        Assert.Null(state.PendingOperation);
    }
}
