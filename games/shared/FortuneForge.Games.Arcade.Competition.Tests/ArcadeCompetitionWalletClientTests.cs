using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Arcade.Competition;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Games.Arcade.Competition.Tests;

public sealed class ArcadeCompetitionWalletClientTests
{
    private const string Game = "asteroids";
    private const string Account = "arcade-wallet-test";
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 4, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
    private static readonly ArcadeCompetitionEntryDebit Entry = new(Game, Account, "wallet-entry-test",
        new string('a', 64), 75, Now.AddHours(-2));

    [Fact]
    public async Task EachOperationUsesItsOwnSignedRouteAndFrozenWireTerms()
    {
        var wallet = new WalletHandler();
        await using var provider = Services(wallet);
        var entries = provider.GetRequiredService<IArcadeCompetitionEntryWallet>();
        var payout = provider.GetRequiredService<IArcadeCompetitionPayoutCreditor>();
        var competition = new ArcadeCompetitionIdentity(Game, ArcadeCompetitionWindowKind.Daily, Now.AddDays(-1), Now);
        var window = new ArcadeCompetitionWindow(competition.WindowKind, competition.StartsAtUtc, competition.EndsAtUtc);
        var plan = ArcadeCompetitionPayoutPlan.Create(competition, ArcadeCompetitionRules.Evaluate(window,
            [new ArcadeCompetitionAttempt(Account, 0, Entry.EntryFeeCents)]));
        var instruction = Assert.Single(plan.Instructions);

        var receipt = await entries.ChargeAsync(new(Entry, new string('b', 64)), default);
        Assert.Equal(Entry.TransactionId, receipt.WagerTransactionId);
        Assert.Equal(425, receipt.BalanceAfterCents);
        Assert.False(await entries.VerifyLegacyChargeAsync(Entry, default));
        var credited = await payout.CreditAsync(competition, instruction, Now, default);
        Assert.Equal(ArcadeCompetitionPayoutPlan.WalletTransactionId(instruction.IdempotencyKey), credited.TransactionId);

        Assert.Equal(new[]
        {
            ArcadeCompetitionWalletProtocol.EntryRoute(Game), ArcadeCompetitionWalletProtocol.EntryProofRoute(Game),
            ArcadeCompetitionWalletProtocol.PayoutRoute(Game),
        }, wallet.Paths);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RejectsAnUnboundResponseOrMismatchedLedger(bool invalidSha, bool invalidLedger)
    {
        await using var provider = Services(new WalletHandler(invalidSha, invalidLedger));
        var entries = provider.GetRequiredService<IArcadeCompetitionEntryWallet>();
        await Assert.ThrowsAsync<GameWalletUnavailableException>(() => entries.ChargeAsync(new(Entry, new string('b', 64)), default));
    }

    private static ServiceProvider Services(WalletHandler wallet)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddSingleton<TimeProvider>(new FixedClock());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["GameWallet:SigningKey"] = Convert.ToBase64String(Key), ["GameWallet:Url"] = "https://wallet.invalid" }).Build());
        services.AddArcadeCompetitionWallet(Game).ConfigurePrimaryHttpMessageHandler(() => wallet);
        return services.BuildServiceProvider();
    }

    private sealed class WalletHandler(bool invalidSha = false, bool invalidLedger = false) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken token)
        {
            var path = message.RequestUri!.AbsolutePath;
            Paths.Add(path);
            var body = await message.Content!.ReadAsByteArrayAsync(token);
            var request = new DefaultHttpContext().Request;
            request.Method = message.Method.Method;
            request.Path = path;
            request.ContentLength = body.Length;
            request.Body = new MemoryStream(body);
            request.Headers[GameWalletAuthentication.AssertionHeader] = Assert.Single(message.Headers.GetValues(GameWalletAuthentication.AssertionHeader));
            var signed = await GameWalletAuthentication.VerifyRequestAsync(request, Game, Key, Now, token);
            Assert.NotNull(signed);
            Assert.Equal(Account, signed.Value.AccountId);

            object reply;
            if (path == ArcadeCompetitionWalletProtocol.EntryRoute(Game))
            {
                var charge = JsonSerializer.Deserialize<ArcadeCompetitionEntryCharge>(body, GameWalletAuthentication.WireJson);
                Assert.Equal(Entry, charge?.Entry);
                Assert.Equal(new string('b', 64), charge?.OutcomeSha256);
                reply = new GameWalletSettlementReceipt(GameWalletSettlementIdentity.CreateId(Game, Account, Entry.AttemptDocumentId),
                    invalidLedger ? "wrong-ledger" : Entry.TransactionId, null, 500, 425, 425, Now, false, false);
            }
            else if (path == ArcadeCompetitionWalletProtocol.EntryProofRoute(Game))
            {
                Assert.Equal(Entry, JsonSerializer.Deserialize<ArcadeCompetitionEntryDebit>(body, GameWalletAuthentication.WireJson));
                reply = new ArcadeCompetitionEntryProof(false);
            }
            else
            {
                Assert.Equal(ArcadeCompetitionWalletProtocol.PayoutRoute(Game), path);
                var charge = JsonSerializer.Deserialize<ArcadeCompetitionPayoutCharge>(body, GameWalletAuthentication.WireJson);
                Assert.True(ArcadeCompetitionWalletProtocol.IsPayoutCharge(Game, Account, charge));
                Assert.Equal(75, charge!.Instruction.AmountCents);
                reply = new ArcadeCompetitionPayoutCreditResult(
                    ArcadeCompetitionPayoutPlan.WalletTransactionId(charge.Instruction.IdempotencyKey), 500, Now, false);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(reply, options: GameWalletAuthentication.WireJson) };
            response.Headers.Add(GameWalletAuthentication.RequestSha256Header,
                invalidSha ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(body)));
            return response;
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "ArcadeWalletTests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
