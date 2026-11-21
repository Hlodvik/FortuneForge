using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Arcade.Competition;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Accounts.Wallet;
using FortuneForge.Server.Arcade.Competition;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Server.Tests.Accounts;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class ArcadeCompetitionWalletRpcTests(SolitaireFirestoreEmulatorFixture fixture)
{
    private const string Game = "asteroids";
    private const string Player = "wallet-arcade-player";
    private const string OtherPlayer = "wallet-arcade-other";
    private static readonly DateTimeOffset End = new(2026, 11, 21, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Signed_entry_retries_preserve_exact_legacy_ledger_identity_and_charge_once()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWalletAsync(database, key);
        using var worker = Worker(app, key);
        var entries = worker.GetRequiredService<IArcadeCompetitionEntryWallet>();
        await SeedBalanceAsync(database, Player, 500);
        var debit = new ArcadeCompetitionEntryDebit(Game, Player, "signed-entry", new string('a', 64), 75, End.AddMinutes(-1));
        var charge = new ArcadeCompetitionEntryCharge(debit, new string('b', 64));

        var first = await entries.ChargeAsync(charge, default);
        var replay = await entries.ChargeAsync(charge, default);
        Assert.Equal(first with { WasAlreadySettled = true }, replay);
        Assert.False(first.InsufficientFunds);
        Assert.False(first.WasAlreadySettled);
        Assert.Equal(debit.TransactionId, first.WagerTransactionId);
        Assert.Equal(425L, first.BalanceAfterCents);
        var ledger = Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(debit.TransactionId, ledger.Id);
        Assert.Equal("arcade-competition-entry", ledger.GetValue<string>("type"));
        Assert.Equal(Player, ledger.GetValue<string>("userId"));
        Assert.Equal(debit.AttemptId, ledger.GetValue<string>("idempotencyKey"));
        Assert.Equal(-75L, ledger.GetValue<long>("amountCents"));
        Assert.Equal(-0.75d, ledger.GetValue<double>("amount"));
        Assert.Equal(first.SettlementId, ledger.GetValue<string>("walletSettlementId"));
        Assert.Equal(425L, await BalanceAsync(database, Player));
        Assert.Single((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);

        await Assert.ThrowsAsync<GameWalletConflictException>(() => entries.ChargeAsync(
            charge with { Entry = debit with { EntryFeeCents = 76 } }, default));
        app.Configuration[$"GameWallet:Games:{Game}:AllowArcadeCompetition"] = "false";
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => entries.ChargeAsync(charge, default));
        Assert.Equal(425L, await BalanceAsync(database, Player));
    }

    [Fact]
    public async Task Legacy_entry_proof_requires_saved_terms_and_exact_signed_account()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWalletAsync(database, key);
        using var worker = Worker(app, key);
        var entries = worker.GetRequiredService<IArcadeCompetitionEntryWallet>();
        var competitions = app.Services.GetRequiredService<IArcadeCompetitionStore>();
        var saved = (await competitions.StartAttemptAsync(new("legacy-entry", Competition(), Player, 75, End.AddMinutes(-1)), default)).Attempt;
        var debit = new ArcadeCompetitionEntryDebit(Game, Player, saved.AttemptId, saved.DocumentId, saved.EntryFeeCents, saved.EnteredAtUtc);
        Assert.False(await entries.VerifyLegacyChargeAsync(debit, default));

        // Historical ledger rows stored the Rand amount and attempt key without a new wallet receipt.
        var legacy = database.Collection("balanceTransactions").Document(debit.TransactionId);
        await legacy.CreateAsync(new Dictionary<string, object>
        {
            ["transactionId"] = debit.TransactionId,
            ["userId"] = Player,
            ["currencyId"] = "slotsCredits",
            ["type"] = "arcade-competition-entry",
            ["idempotencyKey"] = debit.AttemptId,
            ["amount"] = -0.75d,
        });
        Assert.True(await entries.VerifyLegacyChargeAsync(debit, default));
        Assert.False(await entries.VerifyLegacyChargeAsync(debit with { PlayerId = OtherPlayer }, default));
        Assert.False(await entries.VerifyLegacyChargeAsync(debit with { EnteredAtUtc = debit.EnteredAtUtc.AddTicks(10) }, default));

        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var route = ArcadeCompetitionWalletProtocol.EntryProofRoute(Game);
        var body = JsonSerializer.SerializeToUtf8Bytes(debit, GameWalletAuthentication.WireJson);
        using var mismatched = new HttpRequestMessage(HttpMethod.Post, route) { Content = new ByteArrayContent(body) };
        mismatched.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        mismatched.Headers.Add(GameWalletAuthentication.AssertionHeader,
            GameWalletAuthentication.CreatePostAssertion(Game, OtherPlayer, route, body, key, DateTimeOffset.UtcNow));
        using var refused = await http.SendAsync(mismatched);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        await legacy.UpdateAsync("amount", -0.76d);
        Assert.False(await entries.VerifyLegacyChargeAsync(debit, default));
        Assert.Empty((await database.Collection("walletRoundSettlements").GetSnapshotAsync()).Documents);
        Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Payout_route_credits_only_the_saved_instruction_and_replays_the_original_credit()
    {
        var database = Database();
        var key = RandomNumberGenerator.GetBytes(32);
        await using var app = await StartWalletAsync(database, key);
        using var worker = Worker(app, key);
        var creditor = worker.GetRequiredService<IArcadeCompetitionPayoutCreditor>();
        var competitions = app.Services.GetRequiredService<IArcadeCompetitionStore>();
        var competition = Competition();
        var plan = Plan(competition, 100);
        var instruction = Assert.Single(plan.Instructions, item => item.PlayerId == Player);
        var changed = Assert.Single(Plan(competition, 200).Instructions, item => item.PlayerId == Player);
        Assert.NotEqual(instruction.AmountCents, changed.AmountCents);
        Assert.True(ArcadeCompetitionWalletProtocol.IsPayoutCharge(Game, Player, new(competition, changed, End)));
        await SeedBalanceAsync(database, Player, 500);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => creditor.CreditAsync(competition, instruction, End, default));
        await competitions.StoreSettlementPlanAsync(plan, End, default);
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => creditor.CreditAsync(competition, changed, End, default));
        Assert.Equal(500L, await BalanceAsync(database, Player));
        Assert.Empty((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);

        var first = await creditor.CreditAsync(competition, instruction, End.AddMinutes(1), default);
        var retry = await creditor.CreditAsync(competition, instruction, End.AddMinutes(2), default);
        Assert.False(first.WasAlreadyCredited);
        Assert.Equal(first with { WasAlreadyCredited = true }, retry);
        Assert.Equal(500 + instruction.AmountCents, first.BalanceAfterCents);
        Assert.Equal(first.BalanceAfterCents, await BalanceAsync(database, Player));
        var ledger = Assert.Single((await database.Collection("balanceTransactions").GetSnapshotAsync()).Documents);
        Assert.Equal(first.TransactionId, ledger.Id);
        Assert.Equal(competition.DocumentId, ledger.GetValue<string>("competitionId"));
        Assert.Equal(instruction.IdempotencyKey, ledger.GetValue<string>("idempotencyKey"));
        Assert.Equal(instruction.AmountCents, ledger.GetValue<long>("amountCents"));
    }

    private static ArcadeCompetitionIdentity Competition() => new(Game, ArcadeCompetitionWindowKind.Daily, End.AddDays(-1), End);

    private static ArcadeCompetitionPayoutPlan Plan(ArcadeCompetitionIdentity competition, long entryFee) =>
        ArcadeCompetitionPayoutPlan.Create(competition, ArcadeCompetitionRules.Evaluate(
            new(competition.WindowKind, competition.StartsAtUtc, competition.EndsAtUtc),
            [new(Player, 20, entryFee), new(OtherPlayer, 5, entryFee)]));

    private FirestoreDb Database()
    {
        _ = fixture;
        return new FirestoreDbBuilder
        {
            ProjectId = $"demo-arcade-wallet-rpc-{Guid.NewGuid():N}",
            EmulatorDetection = EmulatorDetection.EmulatorOnly,
        }.Build();
    }

    private static async Task<WebApplication> StartWalletAsync(FirestoreDb database, byte[] key)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"GameWallet:Games:{Game}:SigningKey"] = Convert.ToBase64String(key),
            [$"GameWallet:Games:{Game}:AllowArcadeCompetition"] = "true",
        });
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<IWalletRoundSettlementStore>(new FirestoreWalletRoundSettlementStore(database));
        builder.Services.AddSingleton<IArcadeCompetitionStore>(new FirestoreArcadeCompetitionStore(database));
        builder.Services.AddSingleton<IArcadeCompetitionPayoutCreditor>(new FirestoreArcadeCompetitionPayoutCreditor(new FirestoreWalletCreditStore(database)));
        var app = builder.Build();
        app.MapGameWalletSettlement();
        app.MapArcadeCompetitionWallet();
        await app.StartAsync();
        return app;
    }

    private static ServiceProvider Worker(WebApplication app, byte[] key)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(app.Environment);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GameWallet:Url"] = app.Urls.Single(),
            ["GameWallet:SigningKey"] = Convert.ToBase64String(key),
        }).Build());
        services.AddArcadeCompetitionWallet(Game);
        return services.BuildServiceProvider();
    }

    private static Task SeedBalanceAsync(FirestoreDb database, string player, long cents) =>
        database.Collection("userBalances").Document($"{player}_slotsCredits").SetAsync(new Dictionary<string, object>
        {
            ["userId"] = player, ["currencyId"] = "slotsCredits", ["available"] = cents / 100,
            ["availableFractionalCents"] = cents % 100, ["reserved"] = 0L, ["version"] = 1L,
        });

    private static Task<long> BalanceAsync(FirestoreDb database, string player) =>
        FirestoreWalletDocuments.ReadSlotsCreditCentsAsync(database, player, false, default);
}
