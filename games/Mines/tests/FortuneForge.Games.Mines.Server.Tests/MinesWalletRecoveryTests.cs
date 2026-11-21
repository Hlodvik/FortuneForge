using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Mines.Server;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Grpc.Core;

namespace FortuneForge.Games.Mines.Server.Tests;

[Collection("Mines Firestore")]
public sealed class MinesWalletRecoveryTests(MinesEmulator emulator)
{
    [Fact]
    public async Task InconsistentLegacyActiveHead_WithExistingHistoryCannotCreditBeforeValidation()
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = "demo-mines-history-" + Guid.NewGuid().ToString("N"), Endpoint = emulator.Endpoint,
            ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None,
        }.Build();
        var id = MinesService.Hash("legacy-round");
        var now = DateTimeOffset.UtcNow;
        var state = new MinesRoundState(100, 3, [0, 1, 2], [24], MinesRoundStatus.Active, 0);
        var legacy = new MinesStoredRound(id, "player", state, 2, now, now);
        await database.Collection("minesRounds").Document(id).SetAsync(new Dictionary<string, object>
        {
            ["roundId"] = id, ["userId"] = "player", ["schemaVersion"] = 1L, ["version"] = 2L,
            ["privateRoundJson"] = JsonSerializer.Serialize(legacy),
        });
        await database.Collection("minesSessions").Document(MinesService.Hash("player")).SetAsync(new Dictionary<string, object>
        {
            ["userId"] = "player", ["schemaVersion"] = 1L, ["latestRoundId"] = id,
        });
        await database.Collection("cardGameResults").Document("mines-" + id).SetAsync(new Dictionary<string, object> { ["userId"] = "player" });
        var wallet = new MinesTestWallet(database);
        var service = new MinesService(new FirestoreMinesStore(database, wallet, wallet));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CashOutAsync("player", id, new("legacy-duplicate-cashout-0001")));
        Assert.Empty((await database.Collection("minesTestWalletReceipts").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task InsufficientPreflightReply_RemainsDeclinedAfterTopUp()
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = "demo-mines-decline-" + Guid.NewGuid().ToString("N"), Endpoint = emulator.Endpoint,
            ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None,
        }.Build();
        var wallet = new MinesTestWallet(database);
        var boards = new MinesStoreTests.FixedBoard(3);
        var service = new MinesService(new FirestoreMinesStore(database, wallet, wallet), new MinesEngine(boards));
        var request = new StartMinesRequest("mines-poor-request-0001", 100, 3);
        await Assert.ThrowsAsync<MinesInsufficientFundsException>(() => service.StartAsync("player", request));
        await database.Collection("userBalances").Document("player_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["available"] = 10L, ["availableFractionalCents"] = 0L, ["version"] = 1L,
        });
        await Assert.ThrowsAsync<MinesInsufficientFundsException>(() => service.StartAsync("player", request));
        Assert.Equal(0, boards.Calls);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(900, (await service.StartAsync("player", request with { RequestId = "mines-funded-request-0002" })).BalanceCents);
    }

    [Fact]
    public async Task LostReservationAndCompletionReplies_RecoverPrivateBoardAndOnePaymentAfterRestartAndPause()
    {
        var database = new FirestoreDbBuilder
        {
            ProjectId = "demo-mines-wallet-" + Guid.NewGuid().ToString("N"), Endpoint = emulator.Endpoint,
            ChannelCredentials = ChannelCredentials.Insecure, EmulatorDetection = EmulatorDetection.None,
        }.Build();
        await database.Collection("userBalances").Document("player_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["available"] = 1000L, ["availableFractionalCents"] = 0L, ["version"] = 1L,
        });
        var wallet = new MinesTestWallet(database) { DropNextReply = true };
        var enabled = true;
        MinesService Service() => new(new FirestoreMinesStore(database, wallet, wallet, () => enabled),
            new MinesEngine(new MinesStoreTests.FixedBoard(3)));
        var startRequest = new StartMinesRequest("mines-lost-start-request-0001", 123, 3);
        using (GameSecurityContext.Push(new("original-mines-reserve", "player", null, null, DateTime.UtcNow,
            "POST", "/api/games/mines/rounds", "mines", null, startRequest.RequestId, null, "{}", "captured", "test")))
            await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service().StartAsync("player", startRequest));
        Assert.Equal(99_877, (await wallet.GetStateAsync("player", default)).SlotsCreditsBalanceCents);
        Assert.Empty((await database.Collection("minesRounds").GetSnapshotAsync()).Documents);
        enabled = false;
        var recovered = await Service().SessionAsync("player");
        var original = await Service().StartAsync("player", startRequest);
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(recovered));
        Assert.All(recovered.Round!.Tiles, tile => Assert.Equal("hidden", tile.Kind));
        Assert.DoesNotContain("MinePositions", JsonSerializer.Serialize(recovered), StringComparison.OrdinalIgnoreCase);
        await Service().RevealAsync("player", recovered.Round.RoundId, new("mines-lost-reveal-request-0001", 24));
        wallet.DropNextReply = true;
        var cashout = new CashOutMinesRequest("mines-lost-cashout-request-0001");
        using (GameSecurityContext.Push(new("original-mines-payout", "player", null, null, DateTime.UtcNow,
            "POST", "/api/games/mines/rounds/" + recovered.Round.RoundId + "/cashout", "mines", recovered.Round.RoundId,
            cashout.RequestId, null, "{}", "captured", "test")))
            await Assert.ThrowsAsync<GameWalletUnavailableException>(() => Service().CashOutAsync("player", recovered.Round.RoundId, cashout));
        var final = await Service().SessionAsync("player");
        Assert.Equal("cashed-out", final.Round!.Status);
        Assert.Equal(100_002, final.BalanceCents);
        var retries = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Service().CashOutAsync("player", final.Round.RoundId, cashout)));
        Assert.All(retries, result => Assert.Equal(JsonSerializer.Serialize(final), JsonSerializer.Serialize(result)));
        Assert.Equal(2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
        Assert.Single((await database.Collection("cardGameResults").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("gameRoundAudits").GetSnapshotAsync()).Documents);
        var observations = (await database.Collection("gameSecurityEvents").GetSnapshotAsync()).Documents;
        Assert.Contains(observations, item => item.GetValue<string>("requestId") == "original-mines-reserve");
        Assert.Contains(observations, item => item.GetValue<string>("requestId") == "original-mines-payout");
        await Assert.ThrowsAsync<MinesAdmissionClosedException>(() => Service().StartAsync("player", startRequest with { RequestId = "mines-paused-start-request-0002" }));
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(await Service().StartAsync("player", startRequest)));
        var command = database.Collection("minesCommands").Document(MinesService.Hash("player\n" + cashout.RequestId));
        var stored = await command.GetSnapshotAsync();
        var badReply = JsonSerializer.Deserialize<MinesSessionResponse>(stored.GetValue<string>("responseJson"))! with { BalanceCents = 999_999 };
        await command.UpdateAsync("responseJson", JsonSerializer.Serialize(badReply));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CashOutAsync("player", final.Round.RoundId, cashout));
        await command.UpdateAsync(new Dictionary<string, object>
        {
            ["responseJson"] = JsonSerializer.Serialize(final), ["pendingJson"] = "{",
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().CashOutAsync("player", final.Round.RoundId, cashout));
    }
}
