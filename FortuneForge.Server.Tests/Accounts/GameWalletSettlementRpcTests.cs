using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.Hosting;
using FortuneForge.Games.Wukong;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Server.Tests.Accounts;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class GameWalletSettlementRpcTests(SolitaireFirestoreEmulatorFixture fixture)
{
    private const string Game = ArcaneArchivesEngine.GameId;
    private const string Player = "rpc-wallet-player";
    private const string OtherPlayer = "rpc-wallet-other-player";

    [Fact]
    public async Task SignedArcaneRpcSettlesAndReplaysExactBodyForTheAuthenticatedWallet()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key);
        using var workerServices = CreateWorkerClient(app, key);
        var wallet = workerServices.GetRequiredService<IGameWalletClient>();
        await Seed(database, Player, 5, freeGames: 2);
        await Seed(database, OtherPlayer, 1);

        var request = Operation(wagerCents: 125, payoutCents: 297);
        Assert.Equal(JsonSerializer.Serialize(new { request.OperationId, request.OutcomeSha256,
            request.WagerCents, request.PayoutCents, request.SpendLegacyFreeGame }, GameWalletAuthentication.WireJson),
            JsonSerializer.Serialize(request, GameWalletAuthentication.WireJson));
        var receipt = await wallet.SettleAsync(Player, request, default);
        Assert.DoesNotContain("legacyFreeGamesCountMismatch", JsonSerializer.Serialize(receipt, GameWalletAuthentication.WireJson));
        Assert.Equal(GameWalletSettlementIdentity.CreateId(Game, Player, request.OperationId), receipt.SettlementId);
        Assert.Equal($"{request.OperationId}-wager", receipt.WagerTransactionId);
        Assert.Equal($"{request.OperationId}-payout", receipt.PayoutTransactionId);
        Assert.Equal(500, receipt.BalanceBeforeCents);
        Assert.Equal(375, receipt.BalanceAfterWagerCents);
        Assert.Equal(672, receipt.BalanceAfterCents);
        Assert.Equal(2, receipt.LegacyFreeGamesRemaining);
        Assert.Null(receipt.LegacyFreeGameTransactionId);
        Assert.False(receipt.WasAlreadySettled);

        var replay = await wallet.SettleAsync(Player, request, default);
        Assert.Equal(receipt with { WasAlreadySettled = true }, replay);
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player,
            request with { PayoutCents = request.PayoutCents + 1 }, default));

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var reencoded = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, GameWalletAuthentication.WireJson) + "\n");
        using var encodingConflict = await Send(http, reencoded, Sign(Player, reencoded, key));
        Assert.Equal(HttpStatusCode.Conflict, encodingConflict.StatusCode);

        var pendingWin = Operation(wagerCents: 200, payoutCents: 9_000);
        var declined = await wallet.SettleAsync(OtherPlayer, pendingWin, default);
        Assert.True(declined.InsufficientFunds);
        Assert.False(declined.InsufficientLegacyFreeGames);
        Assert.Equal(100, declined.BalanceAfterCents);
        Assert.Null(declined.WagerTransactionId);
        Assert.Null(declined.PayoutTransactionId);
        Assert.Equal(672, await Balance(database, Player));
        Assert.Equal(100, await Balance(database, OtherPlayer));
        Assert.Equal(2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task SignedStartingBalanceMinimumDeclinesWithoutPostingAndSharesAnImmutableReceipt()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key);
        using var workerServices = CreateWorkerClient(app, key);
        var wallet = workerServices.GetRequiredService<IGameWalletClient>();
        await Seed(database, Player, 5);
        var terms = Operation(wagerCents: 125, payoutCents: 297) with { MinimumBalanceBeforeCents = 501 };

        var declined = await wallet.SettleAsync(Player, terms, default);
        Assert.True(declined.InsufficientFunds);
        Assert.Equal(500, declined.BalanceBeforeCents);
        Assert.Equal(500, declined.BalanceAfterCents);
        Assert.Null(declined.WagerTransactionId);
        Assert.Null(declined.PayoutTransactionId);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(500, await Balance(database, Player));

        await Seed(database, Player, 6);
        Assert.Equal(declined with { WasAlreadySettled = true }, await wallet.SettleAsync(Player, terms, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player,
            terms with { MinimumBalanceBeforeCents = null }, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player,
            terms with { MinimumBalanceBeforeCents = 500 }, default));

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var invalid = JsonSerializer.SerializeToUtf8Bytes(terms with { MinimumBalanceBeforeCents = -1 }, GameWalletAuthentication.WireJson);
        using var negative = await Send(http, invalid, Sign(Player, invalid, key));
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        var accepted = await wallet.SettleAsync(Player,
            Operation(wagerCents: 125, payoutCents: 297) with { MinimumBalanceBeforeCents = 600 }, default);
        Assert.False(accepted.InsufficientFunds);
        Assert.Equal(600, accepted.BalanceBeforeCents);
        Assert.Equal(475, accepted.BalanceAfterWagerCents);
        Assert.Equal(772, accepted.BalanceAfterCents);
        Assert.Equal(2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task RpcRejectsCookiesWorkerKeyAndSpoofedOrTamperedBody()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key);
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        await Seed(database, Player, 5);
        await Seed(database, OtherPlayer, 2);
        var request = Operation(wagerCents: 125, payoutCents: 297);
        var body = JsonSerializer.SerializeToUtf8Bytes(request, GameWalletAuthentication.WireJson);

        using var browser = await Send(http, body, assertion: null, cookie: "fortuna_session=browser-cookie");
        Assert.Equal(HttpStatusCode.Forbidden, browser.StatusCode);
        using var wrongKey = await Send(http, body, Sign(Player, body, RandomNumberGenerator.GetBytes(32)));
        Assert.Equal(HttpStatusCode.Forbidden, wrongKey.StatusCode);
        using var wrongScope = await Send(http, body, GameWalletAuthentication.CreateSettlementAssertion("neon-nights-v1",
            Player, body, key, DateTimeOffset.UtcNow));
        Assert.Equal(HttpStatusCode.Forbidden, wrongScope.StatusCode);
        using var ingress = await Send(http, body,
            GameWorkerAuthentication.CreateAssertion(Game, Player, "POST", GameWalletAuthentication.SettlementRoute(Game),
                body, key, DateTimeOffset.UtcNow), GameWorkerAuthentication.AssertionHeader);
        Assert.Equal(HttpStatusCode.Forbidden, ingress.StatusCode);

        var spoofed = JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.OperationId, request.OutcomeSha256, request.WagerCents, request.PayoutCents, userId = OtherPlayer,
        }, GameWalletAuthentication.WireJson);
        using var spoof = await Send(http, spoofed, Sign(Player, spoofed, key));
        Assert.Equal(HttpStatusCode.BadRequest, spoof.StatusCode);
        using var mismatch = await Send(http, spoofed, Sign(Player, body, key));
        Assert.Equal(HttpStatusCode.Forbidden, mismatch.StatusCode);
        var forbiddenQuery = $"{GameWalletAuthentication.SettlementRoute(Game)}?account={OtherPlayer}";
        using var query = await Send(http, body, Sign(Player, body, key), path: forbiddenQuery);
        Assert.Equal(HttpStatusCode.NotFound, query.StatusCode);

        Assert.Equal(500, await Balance(database, Player));
        Assert.Equal(200, await Balance(database, OtherPlayer));
        Assert.Empty((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task SignedLegacyFreeGameConsumesOneUnitAndPaysOnceWithTerminalShortfall()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key);
        using var workerServices = CreateWorkerClient(app, key);
        var wallet = workerServices.GetRequiredService<IGameWalletClient>();
        await Seed(database, Player, 5, freeGames: 1);

        var request = Operation(wagerCents: 0, payoutCents: 297) with { SpendLegacyFreeGame = true };
        var receipt = await wallet.SettleAsync(Player, request, default);
        Assert.False(receipt.InsufficientFunds);
        Assert.False(receipt.InsufficientLegacyFreeGames);
        Assert.Null(receipt.WagerTransactionId);
        Assert.Equal($"{request.OperationId}-payout", receipt.PayoutTransactionId);
        Assert.Equal($"{request.OperationId}-free-game-use", receipt.LegacyFreeGameTransactionId);
        Assert.Equal(0, receipt.LegacyFreeGamesRemaining);
        Assert.Equal(797, receipt.BalanceAfterCents);
        Assert.Equal(0, (await Entitlement(database, Player).GetSnapshotAsync()).GetValue<long>("available"));

        var second = Operation(wagerCents: 0, payoutCents: 900) with { SpendLegacyFreeGame = true };
        var declined = await wallet.SettleAsync(Player, second, default);
        Assert.True(declined.InsufficientLegacyFreeGames);
        Assert.False(declined.InsufficientFunds);
        Assert.Null(declined.PayoutTransactionId);
        Assert.Null(declined.LegacyFreeGameTransactionId);
        Assert.Equal(0, declined.LegacyFreeGamesRemaining);
        await Entitlement(database, Player).UpdateAsync("available", 1L);

        Assert.Equal(receipt with { WasAlreadySettled = true }, await wallet.SettleAsync(Player, request, default));
        Assert.Equal(declined with { WasAlreadySettled = true }, await wallet.SettleAsync(Player, second, default));
        Assert.Equal(797, await Balance(database, Player));
        Assert.Equal(1, (await Entitlement(database, Player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(2, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);

        app.Configuration[$"GameWallet:Games:{Game}:AllowLegacyFreeGames"] = "false";
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => wallet.SettleAsync(Player,
            Operation(wagerCents: 0, payoutCents: 500) with { SpendLegacyFreeGame = true }, default));
        Assert.Equal(797, await Balance(database, Player));
    }

    [Fact]
    public async Task ChargedLegacyGameNeedsOptInAndSettlesAllThreeLegsOnce()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key);
        using var workerServices = CreateWorkerClient(app, key);
        var wallet = workerServices.GetRequiredService<IGameWalletClient>();
        await Seed(database, Player, 5, freeGames: 2);
        var request = Operation(wagerCents: 125, payoutCents: 297) with
        {
            SpendLegacyFreeGame = true,
            ExpectedLegacyFreeGamesRemaining = 2,
        };

        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => wallet.SettleAsync(Player, request, default));
        Assert.Empty((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);
        app.Configuration[$"GameWallet:Games:{Game}:AllowCashWagerWithLegacyFreeGame"] = "true";
        var receipt = await wallet.SettleAsync(Player, request, default);

        Assert.False(receipt.LegacyFreeGamesCountMismatch);
        Assert.Equal($"{request.OperationId}-wager", receipt.WagerTransactionId);
        Assert.Equal($"{request.OperationId}-payout", receipt.PayoutTransactionId);
        Assert.Equal($"{request.OperationId}-free-game-use", receipt.LegacyFreeGameTransactionId);
        Assert.Equal(672, receipt.BalanceAfterCents);
        Assert.Equal(1, receipt.LegacyFreeGamesRemaining);
        Assert.Equal(1, (await Entitlement(database, Player).GetSnapshotAsync()).GetValue<long>("available"));

        var tooExpensive = Operation(wagerCents: 800, payoutCents: 9_000) with
        {
            SpendLegacyFreeGame = true,
            ExpectedLegacyFreeGamesRemaining = 1,
        };
        var declined = await wallet.SettleAsync(Player, tooExpensive, default);
        Assert.True(declined.InsufficientFunds);
        Assert.False(declined.LegacyFreeGamesCountMismatch);
        Assert.Null(declined.LegacyFreeGameTransactionId);
        Assert.Equal(1, declined.LegacyFreeGamesRemaining);
        Assert.Equal(1, (await Entitlement(database, Player).GetSnapshotAsync()).GetValue<long>("available"));

        Assert.Equal(receipt with { WasAlreadySettled = true }, await wallet.SettleAsync(Player, request, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player,
            request with { ExpectedLegacyFreeGamesRemaining = 1 }, default));
        Assert.Equal(672, await Balance(database, Player));
        Assert.Equal(3, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task WukongUsesItsHistoricalFreeBankAndKeepsSignedGameAndAccountScope()
    {
        var database = Database();
        var arcaneKey = RandomNumberGenerator.GetBytes(32);
        var wukongKey = RandomNumberGenerator.GetBytes(32);
        const string wukong = WukongEngine.GameId;
        const string historical = WukongEngine.LegacyStorageGameId;
        const string oldCurrency = "freeGames:" + historical;
        await using var app = await StartWallet(database, arcaneKey);
        app.Configuration[$"GameWallet:Games:{wukong}:SigningKey"] = Convert.ToBase64String(wukongKey);
        app.Configuration[$"GameWallet:Games:{wukong}:AllowLegacyFreeGames"] = "true";
        app.Configuration[$"GameWallet:Games:{wukong}:AllowAccountBalanceRead"] = "true";
        app.Configuration[$"GameWallet:Games:{wukong}:LegacySlotLedger"] = "true";
        using var wukongWorker = CreateWorkerClient(app, wukongKey, wukong);
        using var arcaneWorker = CreateWorkerClient(app, arcaneKey);
        var balances = database.Collection("userBalances");
        var oldBank = balances.Document($"{Player}_{oldCurrency}");
        var otherOldBank = balances.Document($"{OtherPlayer}_{oldCurrency}");
        var unusedCanonicalBank = balances.Document($"{Player}_freeGames:{wukong}");
        await Task.WhenAll(
            Seed(database, Player, 5, freeGames: 9),
            Seed(database, OtherPlayer, 7),
            oldBank.SetAsync(new Dictionary<string, object> { ["available"] = 3L, ["userId"] = Player, ["currencyId"] = oldCurrency }),
            otherOldBank.SetAsync(new Dictionary<string, object> { ["available"] = 1L, ["userId"] = OtherPlayer }),
            unusedCanonicalBank.SetAsync(new Dictionary<string, object> { ["available"] = 91L }));

        var reader = wukongWorker.GetRequiredService<IGameWalletAccountReader>();
        Assert.Equal(new GameWalletAccountState(3, 500), await reader.GetStateAsync(Player, default));
        Assert.Equal(new GameWalletAccountState(1, 700), await reader.GetStateAsync(OtherPlayer, default));
        Assert.Equal(new GameWalletAccountState(9), await arcaneWorker.GetRequiredService<IGameWalletAccountReader>().GetStateAsync(Player, default));

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var wrongGame = new HttpRequestMessage(HttpMethod.Get, GameWalletAuthentication.AccountStateRoute(Game));
        wrongGame.Headers.Add(GameWalletAuthentication.AssertionHeader,
            GameWalletAuthentication.CreateAccountStateAssertion(wukong, Player, wukongKey, DateTimeOffset.UtcNow));
        using var rejected = await http.SendAsync(wrongGame);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        using var wrongAlias = new HttpRequestMessage(HttpMethod.Get, GameWalletAuthentication.AccountStateRoute(historical));
        wrongAlias.Headers.Add(GameWalletAuthentication.AssertionHeader,
            GameWalletAuthentication.CreateAccountStateAssertion(historical, Player, wukongKey, DateTimeOffset.UtcNow));
        using var aliasRejected = await http.SendAsync(wrongAlias);
        Assert.Equal(HttpStatusCode.NotFound, aliasRejected.StatusCode);

        var request = Operation(wagerCents: 0, payoutCents: 75) with
        {
            SpendLegacyFreeGame = true,
            ExpectedLegacyFreeGamesRemaining = 3,
        };
        var wallet = wukongWorker.GetRequiredService<IGameWalletClient>();
        var wrongAccount = await wallet.SettleAsync(OtherPlayer, request, default);
        Assert.True(wrongAccount.LegacyFreeGamesCountMismatch);
        Assert.Null(wrongAccount.LegacyFreeGameTransactionId);
        Assert.Equal(1, wrongAccount.LegacyFreeGamesRemaining);
        var settled = await wallet.SettleAsync(Player, request, default);
        Assert.Equal(GameWalletSettlementIdentity.CreateId(wukong, Player, request.OperationId), settled.SettlementId);
        Assert.Equal($"{request.OperationId}-free-game-use", settled.LegacyFreeGameTransactionId);
        Assert.Equal(2, settled.LegacyFreeGamesRemaining);
        Assert.Equal(575, settled.BalanceAfterCents);
        Assert.Equal(settled with { WasAlreadySettled = true }, await wallet.SettleAsync(Player, request, default));
        Assert.Equal(new GameWalletAccountState(2, 575), await reader.GetStateAsync(Player, default));
        Assert.Equal(1, (await otherOldBank.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(91, (await unusedCanonicalBank.GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(9, (await Entitlement(database, Player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(oldCurrency, (await database.Collection("balanceTransactions")
            .Document(settled.LegacyFreeGameTransactionId!).GetSnapshotAsync()).GetValue<string>("currencyId"));
    }

    [Fact]
    public async Task ReservationChargesBeforeCompletionAndBothPhasesReplayAfterLostAcknowledgement()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key);
        using var worker = CreateWorkerClient(app, key);
        var wallet = worker.GetRequiredService<IGameWalletClient>();
        await Seed(database, Player, 5, freeGames: 2);
        await Seed(database, OtherPlayer, 5, freeGames: 2);
        var reserve = Operation(wagerCents: 125, payoutCents: 0) with
        {
            Phase = GameWalletSettlementPhase.Reserve,
            SpendLegacyFreeGame = true,
            ExpectedLegacyFreeGamesRemaining = 2,
        };
        var complete = CompleteReservation(reserve, payoutCents: 297);
        app.Configuration[$"GameWallet:Games:{Game}:AllowCashWagerWithLegacyFreeGame"] = "true";
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => wallet.SettleAsync(Player, reserve, default));
        app.Configuration[$"GameWallet:Games:{Game}:AllowWagerReservations"] = "true";
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player, complete, default));

        var reserves = await Task.WhenAll(wallet.SettleAsync(Player, reserve, default), wallet.SettleAsync(Player, reserve, default));
        var held = Assert.Single(reserves, item => !item.WasAlreadySettled);
        Assert.Equal(held with { WasAlreadySettled = true }, Assert.Single(reserves, item => item.WasAlreadySettled));
        Assert.Equal(GameWalletSettlementIdentity.CreateId(Game, Player, reserve.OperationId), held.SettlementId);
        Assert.Equal($"{reserve.OperationId}-wager", held.WagerTransactionId);
        Assert.Equal($"{reserve.OperationId}-free-game-use", held.LegacyFreeGameTransactionId);
        Assert.Null(held.PayoutTransactionId);
        Assert.Equal(375, await Balance(database, Player));
        Assert.Equal(1, (await Entitlement(database, Player).GetSnapshotAsync()).GetValue<long>("available"));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player,
            reserve with { Phase = GameWalletSettlementPhase.Direct }, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player,
            reserve with { WagerCents = 126 }, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player,
            complete with { ReservationSha256 = new string('c', 64) }, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(OtherPlayer, complete, default));
        const string otherGame = "another-wallet-scope";
        app.Configuration[$"GameWallet:Games:{otherGame}:SigningKey"] = Convert.ToBase64String(key);
        app.Configuration[$"GameWallet:Games:{otherGame}:AllowWagerReservations"] = "true";
        using var wrongGameWorker = CreateWorkerClient(app, key, otherGame);
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wrongGameWorker.GetRequiredService<IGameWalletClient>()
            .SettleAsync(Player, complete, default));

        await wallet.SettleAsync(Player, Operation(wagerCents: 375, payoutCents: 0), default);
        Assert.Equal(0, await Balance(database, Player));
        var completions = await Task.WhenAll(wallet.SettleAsync(Player, complete, default), wallet.SettleAsync(Player, complete, default));
        var paid = Assert.Single(completions, item => !item.WasAlreadySettled);
        Assert.Equal(paid with { WasAlreadySettled = true }, Assert.Single(completions, item => item.WasAlreadySettled));
        Assert.Equal(GameWalletSettlementIdentity.CreateCompletionId(Game, Player, reserve.OperationId), paid.SettlementId);
        Assert.Equal($"{reserve.OperationId}-payout", paid.PayoutTransactionId);
        Assert.Null(paid.WagerTransactionId);
        Assert.Null(paid.LegacyFreeGameTransactionId);
        Assert.Equal(0, paid.BalanceBeforeCents);
        Assert.Equal(0, paid.BalanceAfterWagerCents);
        Assert.Equal(297, paid.BalanceAfterCents);
        Assert.Equal(held with { WasAlreadySettled = true }, await wallet.SettleAsync(Player, reserve, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player,
            complete with { PayoutCents = 300 }, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player,
            complete with { OutcomeSha256 = new string('d', 64) }, default));
        Assert.Equal(297, await Balance(database, Player));
        Assert.Equal(500, await Balance(database, OtherPlayer));
        Assert.Equal(4, (await database.Collection("balanceTransactions").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task DeclinedReservationCannotBeRevivedAndZeroPayoutClosesASuccessfulHold()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key);
        app.Configuration[$"GameWallet:Games:{Game}:AllowWagerReservations"] = "true";
        using var worker = CreateWorkerClient(app, key);
        var wallet = worker.GetRequiredService<IGameWalletClient>();
        await Seed(database, Player, 1);
        var reserve = Operation(wagerCents: 200, payoutCents: 0) with { Phase = GameWalletSettlementPhase.Reserve };
        var declined = await wallet.SettleAsync(Player, reserve, default);
        Assert.True(declined.InsufficientFunds);
        Assert.Null(declined.WagerTransactionId);
        await Seed(database, Player, 5);
        Assert.Equal(declined with { WasAlreadySettled = true }, await wallet.SettleAsync(Player, reserve, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player,
            CompleteReservation(reserve, payoutCents: 2_000), default));
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => wallet.SettleAsync(Player,
            reserve with { PayoutCents = 1 }, default));
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => wallet.SettleAsync(Player,
            CompleteReservation(reserve, payoutCents: 0) with { ExpectedLegacyFreeGamesRemaining = 0 }, default));

        var direct = Operation(wagerCents: 0, payoutCents: 0);
        await wallet.SettleAsync(Player, direct, default);
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player,
            direct with { Phase = GameWalletSettlementPhase.Reserve }, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player,
            CompleteReservation(direct, payoutCents: 200), default));

        var held = reserve with { OperationId = Guid.NewGuid().ToString("N") };
        await wallet.SettleAsync(Player, held, default);
        var complete = CompleteReservation(held, payoutCents: 0);
        var closed = await wallet.SettleAsync(Player, complete, default);
        Assert.Equal(300, closed.BalanceAfterCents);
        Assert.Null(closed.PayoutTransactionId);
        Assert.Equal(closed with { WasAlreadySettled = true }, await wallet.SettleAsync(Player, complete, default));
        await Assert.ThrowsAsync<GameWalletConflictException>(() => wallet.SettleAsync(Player,
            complete with { PayoutCents = 1 }, default));
        Assert.Equal(300, await Balance(database, Player));
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyCountMismatchIsAnImmutableDeclineWithOrWithoutARequestedSpend(bool spend)
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key);
        using var workerServices = CreateWorkerClient(app, key);
        var wallet = workerServices.GetRequiredService<IGameWalletClient>();
        await Seed(database, Player, 5, freeGames: 2);
        app.Configuration[$"GameWallet:Games:{Game}:AllowCashWagerWithLegacyFreeGame"] = "true";
        var request = Operation(wagerCents: 700, payoutCents: 9_000) with
        {
            SpendLegacyFreeGame = spend,
            ExpectedLegacyFreeGamesRemaining = spend ? 1 : 0,
        };

        var declined = await wallet.SettleAsync(Player, request, default);
        Assert.True(declined.LegacyFreeGamesCountMismatch);
        Assert.False(declined.InsufficientFunds);
        Assert.False(declined.InsufficientLegacyFreeGames);
        Assert.Equal(2, declined.LegacyFreeGamesRemaining);
        Assert.Equal(500, declined.BalanceAfterCents);
        Assert.Null(declined.WagerTransactionId);
        Assert.Null(declined.PayoutTransactionId);
        Assert.Null(declined.LegacyFreeGameTransactionId);

        await Cash(database, Player).UpdateAsync("available", 10L);
        await Entitlement(database, Player).UpdateAsync("available", request.ExpectedLegacyFreeGamesRemaining!.Value);
        Assert.Equal(declined with { WasAlreadySettled = true }, await wallet.SettleAsync(Player, request, default));
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(1_000, await Balance(database, Player));

        var noSpend = Operation(wagerCents: 125, payoutCents: 0) with
        {
            ExpectedLegacyFreeGamesRemaining = request.ExpectedLegacyFreeGamesRemaining,
        };
        var settled = await wallet.SettleAsync(Player, noSpend, default);
        Assert.False(settled.LegacyFreeGamesCountMismatch);
        Assert.False(settled.InsufficientFunds);
        Assert.Null(settled.LegacyFreeGameTransactionId);
        Assert.Equal(request.ExpectedLegacyFreeGamesRemaining, settled.LegacyFreeGamesRemaining);
        Assert.Equal(request.ExpectedLegacyFreeGamesRemaining!.Value,
            (await Entitlement(database, Player).GetSnapshotAsync()).GetValue<long>("available"));
        Assert.Equal(875, await Balance(database, Player));

        app.Configuration[$"GameWallet:Games:{Game}:AllowLegacyFreeGames"] = "false";
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => wallet.SettleAsync(Player,
            request with { OperationId = Guid.NewGuid().ToString("N") }, default));
        Assert.Equal(2, (await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Count);
    }

    [Fact]
    public async Task SignedStateReadsOnlyTheScopedAccountEntitlementAndDoesNotAcceptCookies()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key);
        using var workerServices = CreateWorkerClient(app, key);
        var reader = workerServices.GetRequiredService<IGameWalletAccountReader>();
        await Seed(database, Player, 5, freeGames: 3);
        await Seed(database, OtherPlayer, 2, freeGames: 1);
        await database.Collection("userBalances").Document($"{Player}_freeGames:neon-nights-v1")
            .SetAsync(new Dictionary<string, object> { ["available"] = 9L });

        Assert.Equal(new GameWalletAccountState(3), await reader.GetStateAsync(Player, default));
        Assert.Equal(new GameWalletAccountState(1), await reader.GetStateAsync(OtherPlayer, default));

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var route = GameWalletAuthentication.AccountStateRoute(Game);
        using var browser = new HttpRequestMessage(HttpMethod.Get, route);
        browser.Headers.Add("Cookie", "fortuna_session=browser-cookie");
        using var unsigned = await http.SendAsync(browser);
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);
        using var spoofed = new HttpRequestMessage(HttpMethod.Get, $"{route}?userId={OtherPlayer}");
        spoofed.Headers.Add(GameWalletAuthentication.AssertionHeader,
            GameWalletAuthentication.CreateAccountStateAssertion(Game, Player, key, DateTimeOffset.UtcNow));
        using var spoofResponse = await http.SendAsync(spoofed);
        Assert.Equal(HttpStatusCode.NotFound, spoofResponse.StatusCode);
        using var wrongMethod = new HttpRequestMessage(HttpMethod.Get, route);
        wrongMethod.Headers.Add(GameWalletAuthentication.AssertionHeader, Sign(Player, [], key));
        using var wrongMethodResponse = await http.SendAsync(wrongMethod);
        Assert.Equal(HttpStatusCode.Forbidden, wrongMethodResponse.StatusCode);

        await Cash(database, Player).UpdateAsync("availableFractionalCents", 27L);
        app.Configuration[$"GameWallet:Games:{Game}:AllowAccountBalanceRead"] = "true";
        Assert.Equal(new GameWalletAccountState(3, 527), await reader.GetStateAsync(Player, default));
        Assert.Equal(new GameWalletAccountState(1, 200), await reader.GetStateAsync(OtherPlayer, default));
        app.Configuration[$"GameWallet:Games:{Game}:AllowLegacyFreeGames"] = "false";
        Assert.Equal(new GameWalletAccountState(0, 527), await reader.GetStateAsync(Player, default));
        app.Configuration[$"GameWallet:Games:{Game}:AllowAccountBalanceRead"] = "false";
        Assert.DoesNotContain("slotsCreditsBalanceCents", JsonSerializer.Serialize(new GameWalletAccountState(0),
            GameWalletAuthentication.WireJson));
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => reader.GetStateAsync(Player, default));
        Assert.Empty((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task SignedPlayerNameReadIsOptionalScopedAndIndependentOfWalletBalances()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWallet(database, key);
        using var worker = CreateWorkerClient(app, key);
        var reader = worker.GetRequiredService<IGameWalletAccountReader>();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var people = database.Collection("users");
        await people.Document(Player).SetAsync(new { userId = Player, playerName = "Ázure Fox" });
        await people.Document(OtherPlayer).SetAsync(new { userId = OtherPlayer, playerName = "Silver_Stag" });
        await Seed(database, Player, 5, freeGames: 3);

        using (var initial = await ReadWireAsync(Player))
        {
            Assert.False(initial.RootElement.TryGetProperty("playerName", out _));
            Assert.Equal(3, initial.RootElement.GetProperty("legacyFreeGamesRemaining").GetInt64());
        }

        app.Configuration[$"GameWallet:Games:{Game}:AllowLegacyFreeGames"] = "false";
        app.Configuration[$"GameWallet:Games:{Game}:AllowPlayerNameRead"] = "true";
        Assert.Equal(new GameWalletAccountState(0, PlayerName: "Ázure Fox"), await reader.GetStateAsync(Player, default));
        Assert.Equal(new GameWalletAccountState(0, PlayerName: "Silver_Stag"), await reader.GetStateAsync(OtherPlayer, default));
        using (var namesOnly = await ReadWireAsync(Player))
        {
            Assert.Equal("Ázure Fox", namesOnly.RootElement.GetProperty("playerName").GetString());
            Assert.False(namesOnly.RootElement.TryGetProperty("slotsCreditsBalanceCents", out _));
            Assert.Equal(0, namesOnly.RootElement.GetProperty("legacyFreeGamesRemaining").GetInt64());
        }

        await people.Document(OtherPlayer).UpdateAsync("playerName", 42L);
        Assert.Null((await reader.GetStateAsync(OtherPlayer, default)).PlayerName);
        await people.Document(OtherPlayer).UpdateAsync("playerName", "Bad\nName");
        Assert.Null((await reader.GetStateAsync(OtherPlayer, default)).PlayerName);
        await people.Document(OtherPlayer).SetAsync(new { userId = Player, playerName = "Wrong Account" });
        Assert.Null((await reader.GetStateAsync(OtherPlayer, default)).PlayerName);
        Assert.Null((await reader.GetStateAsync("unknown-account", default)).PlayerName);
        app.Configuration[$"GameWallet:Games:{Game}:AllowPlayerNameRead"] = "false";
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => reader.GetStateAsync(Player, default));

        async Task<JsonDocument> ReadWireAsync(string account)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GameWalletAuthentication.AccountStateRoute(Game));
            request.Headers.Add(GameWalletAuthentication.AssertionHeader,
                GameWalletAuthentication.CreateAccountStateAssertion(Game, account, key, DateTimeOffset.UtcNow));
            using var response = await http.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task WalletClientRejectsAReceiptForAnotherAccountEvenWithMatchingRequestDigest()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var request = Operation(wagerCents: 125, payoutCents: 297);
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(Game, OtherPlayer, request.OperationId),
            "wager", "payout", 500, 375, 672, DateTimeOffset.UtcNow, false, false);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapPost(GameWalletAuthentication.SettlementRoute(Game), async (HttpContext context) =>
        {
            var digest = await SHA256.HashDataAsync(context.Request.Body, context.RequestAborted);
            context.Response.Headers[GameWalletAuthentication.RequestSha256Header] = Convert.ToHexString(digest);
            await context.Response.WriteAsJsonAsync(receipt, GameWalletAuthentication.WireJson);
        });
        await app.StartAsync();
        using var workerServices = CreateWorkerClient(app, key);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => workerServices.GetRequiredService<IGameWalletClient>()
            .SettleAsync(Player, request, default));
    }

    [Theory]
    [InlineData(501L, false)]
    [InlineData(500L, true)]
    public async Task WalletClientRejectsApprovalOrDeclineInconsistentWithStartingBalanceMinimum(long minimum, bool claimedDecline)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var request = Operation(wagerCents: 125, payoutCents: 297) with { MinimumBalanceBeforeCents = minimum };
        var receipt = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(Game, Player, request.OperationId),
            claimedDecline ? null : "wager", claimedDecline ? null : "payout", 500,
            claimedDecline ? 500 : 375, claimedDecline ? 500 : 672, DateTimeOffset.UtcNow, false, claimedDecline);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapPost(GameWalletAuthentication.SettlementRoute(Game), async (HttpContext context) =>
        {
            var digest = await SHA256.HashDataAsync(context.Request.Body, context.RequestAborted);
            context.Response.Headers[GameWalletAuthentication.RequestSha256Header] = Convert.ToHexString(digest);
            await context.Response.WriteAsJsonAsync(receipt, GameWalletAuthentication.WireJson);
        });
        await app.StartAsync();
        using var workerServices = CreateWorkerClient(app, key);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => workerServices.GetRequiredService<IGameWalletClient>()
            .SettleAsync(Player, request, default));
    }

    private FirestoreDb Database()
    {
        _ = fixture;
        return new FirestoreDbBuilder
        {
            ProjectId = $"demo-wallet-rpc-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
    }

    private static async Task<WebApplication> StartWallet(FirestoreDb database, byte[] key)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"GameWallet:Games:{Game}:SigningKey"] = Convert.ToBase64String(key),
            [$"GameWallet:Games:{Game}:LegacySlotLedger"] = "true",
            [$"GameWallet:Games:{Game}:AllowLegacyFreeGames"] = "true",
        });
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<IWalletRoundSettlementStore>(new FirestoreWalletRoundSettlementStore(database));
        var app = builder.Build();
        app.MapGameWalletSettlement();
        await app.StartAsync();
        return app;
    }

    private static ServiceProvider CreateWorkerClient(WebApplication app, byte[] key, string gameId = Game)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(app.Environment);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GameWallet:Url"] = app.Urls.Single(),
            ["GameWallet:SigningKey"] = Convert.ToBase64String(key),
        }).Build());
        services.AddGameWalletClient(gameId);
        return services.BuildServiceProvider();
    }

    private static GameWalletSettlementRequest Operation(long wagerCents, long payoutCents) =>
        new(Guid.NewGuid().ToString("N"), new string('a', 64), wagerCents, payoutCents);

    private static GameWalletSettlementRequest CompleteReservation(GameWalletSettlementRequest reserve, long payoutCents) =>
        new(reserve.OperationId, new string('b', 64), 0, payoutCents, Phase: GameWalletSettlementPhase.CompleteReservation,
            ReservationSha256: reserve.OutcomeSha256);

    private static string Sign(string accountId, byte[] body, byte[] key) =>
        GameWalletAuthentication.CreateSettlementAssertion(Game, accountId, body, key, DateTimeOffset.UtcNow);

    private static async Task<HttpResponseMessage> Send(HttpClient http, byte[] body, string? assertion,
        string header = GameWalletAuthentication.AssertionHeader, string? cookie = null, string? path = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path ?? GameWalletAuthentication.SettlementRoute(Game))
        { Content = new ByteArrayContent(body) };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (assertion is not null) message.Headers.Add(header, assertion);
        if (cookie is not null) message.Headers.Add("Cookie", cookie);
        return await http.SendAsync(message);
    }

    private static DocumentReference Cash(FirestoreDb database, string player) =>
        database.Collection("userBalances").Document($"{player}_slotsCredits");

    private static DocumentReference Entitlement(FirestoreDb database, string player) =>
        database.Collection("userBalances").Document($"{player}_freeGames:{Game}");

    private static async Task Seed(FirestoreDb database, string player, long wholeRand, long? freeGames = null)
    {
        await Cash(database, player).SetAsync(new Dictionary<string, object> { ["available"] = wholeRand });
        if (freeGames is not null)
            await Entitlement(database, player).SetAsync(new Dictionary<string, object> { ["available"] = freeGames.Value });
    }

    private static async Task<long> Balance(FirestoreDb database, string player)
    {
        var balance = await Cash(database, player).GetSnapshotAsync();
        return (balance.GetValue<long>("available") * 100) +
            (balance.TryGetValue<long>("availableFractionalCents", out var cents) ? cents : 0);
    }
}
