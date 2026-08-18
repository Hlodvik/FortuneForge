using System.Reflection;
using System.Text.Json;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Cards.Bots;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Admin.Operations;

public sealed class AdminOperationsContractTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 15, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Controller_ExposesExactlySixGetOnlyRoutes()
    {
        var routes = typeof(AdminOperationsController).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>()
                .Select(attribute => (Method: method.Name, Verb: Assert.Single(attribute.HttpMethods), Route: attribute.Template)))
            .OrderBy(item => item.Route, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(6, routes.Length);
        Assert.All(routes, route => Assert.Equal("GET", route.Verb));
        Assert.Equal(
            new[] { "activity", "bots", "integrity", "matches", "overview", "queues" },
            routes.Select(route => route.Route).ToArray());
    }

    [Fact]
    public async Task DefaultOffGate_Returns503BeforeAuthorizationOrStoreAccess()
    {
        var store = new FakeStore(Snapshot());
        var authorizer = new FakeAuthorizer(AdminOperationsAccessStatus.Authorized);
        var controller = Controller(store, authorizer, enabled: false);

        var result = Assert.IsType<ObjectResult>(await controller.Overview(cancellationToken: CancellationToken.None));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal(0, authorizer.Calls);
        Assert.Equal(0, store.Reads);
        Assert.Empty(store.Audits);
    }

    [Theory]
    [InlineData(1, 401)]
    [InlineData(2, 403)]
    public async Task Authorization_IsServerResolvedAndDeniesBeforeDataAccess(
        int statusValue,
        int expectedStatus)
    {
        var status = (AdminOperationsAccessStatus)statusValue;
        var store = new FakeStore(Snapshot());
        var controller = Controller(store, new FakeAuthorizer(status), enabled: true);

        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.Overview(cancellationToken: CancellationToken.None));

        Assert.Equal(expectedStatus, result.StatusCode);
        Assert.Equal(0, store.Reads);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task AuthorizedAccess_WritesSanitizedAuditAndCalculatesExactRealPlayerPnl()
    {
        var store = new FakeStore(Snapshot(
            financial:
            [
                Financial("slot-1", "slots", 10m, 6m, 4m),
                Financial("blackjack-1", "blackjack", 5m, 7.5m, -2.5m),
                new AdminOperationsFinancialRecord(
                    "solitaire-1", "gaming", "solitaire", "settled", NowUtc.AddMinutes(-1),
                    20m, 18m, 2m, 20m, 2m),
                new AdminOperationsFinancialRecord(
                    "holdem-1", "gaming", "texas-holdem", "settled", NowUtc.AddMinutes(-2),
                    10m, 9m, 1m, 10m, 1m)
            ],
            funding:
            [
                new("purchase-1", "purchase", NowUtc.AddMinutes(-3), 100m),
                new("withdrawal-1", "withdrawal", NowUtc.AddMinutes(-4), 25m)
            ]));
        var controller = Controller(store, new FakeAuthorizer(AdminOperationsAccessStatus.Authorized), enabled: true);

        var response = Assert.IsType<OkObjectResult>(await controller.Overview(cancellationToken: CancellationToken.None));
        var overview = Assert.IsType<AdminOperationsOverviewResponse>(response.Value);

        Assert.Equal(4.5m, overview.HouseGamingNetCredits);
        Assert.Equal(4m, overview.Slots.HouseNetCredits);
        Assert.Equal(-2.5m, overview.Blackjack.HouseNetCredits);
        Assert.Equal(2m, overview.Solitaire.PlatformFeeCredits);
        Assert.Equal(1m, overview.TexasHoldem.PlatformFeeCredits);
        Assert.Equal(100m, overview.Funding.CompletedPurchaseCredits);
        Assert.Equal(25m, overview.Funding.CompletedWithdrawalCredits);
        Assert.Equal("overview", Assert.Single(store.Audits).Operation);
        Assert.Equal("admin-user", Assert.Single(store.Audits).ActorUserId);
    }

    [Fact]
    public async Task BotTelemetry_IsOperationalOnlyAndCannotEnterFinancialTotals()
    {
        var snapshot = Snapshot(
            financial: [Financial("slot-1", "slots", 10m, 6m, 4m)],
            botLeases:
            [
                new("lease-1", "blackjack", NowUtc.AddMinutes(-1), NowUtc.AddMinutes(1), false),
                new("lease-2", "solitaire", NowUtc.AddMinutes(-2), NowUtc.AddMinutes(-1), true)
            ]);
        var store = new FakeStore(snapshot);
        var controller = Controller(store, new FakeAuthorizer(AdminOperationsAccessStatus.Authorized), enabled: true);

        var overviewResult = Assert.IsType<OkObjectResult>(await controller.Overview(cancellationToken: CancellationToken.None));
        var overview = Assert.IsType<AdminOperationsOverviewResponse>(overviewResult.Value);
        var botsResult = Assert.IsType<OkObjectResult>(await controller.Bots(cancellationToken: CancellationToken.None));
        var bots = Assert.IsType<AdminOperationsBotsResponse>(botsResult.Value);

        Assert.Equal(4m, overview.HouseGamingNetCredits);
        Assert.Equal(1, Assert.Single(bots.Games, game => game.Game == "blackjack").ActiveLeases);
        Assert.Contains("excluded", bots.FinancialTreatment, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectedCreditHoldemRevenue_IsReportedAsIntegrityFailure()
    {
        var store = new FakeStore(Snapshot(sourceFindings:
        [
            new AdminOperationsSourceFinding(
                "credit-holdem-revenue-contract",
                "Credit Hold'em revenue must satisfy the real-human pool contract.",
                3,
                2)
        ]));
        var controller = Controller(store, new FakeAuthorizer(AdminOperationsAccessStatus.Authorized), enabled: true);

        var result = Assert.IsType<OkObjectResult>(await controller.Integrity(cancellationToken: CancellationToken.None));
        var response = Assert.IsType<AdminOperationsIntegrityResponse>(result.Value);
        var finding = Assert.Single(response.Checks, check => check.Id == "credit-holdem-revenue-contract");

        Assert.Equal("fail", finding.Status);
        Assert.Equal(3, finding.RecordsChecked);
        Assert.Equal(2, finding.Findings);
    }

    [Fact]
    public async Task BlackjackTotalsAndActivity_CombineSingleGameAndSignedTableLosses()
    {
        var store = new FakeStore(Snapshot(financial:
        [
            Financial("single-game", "blackjack", 5m, 4m, 1m, NowUtc.AddMinutes(-3)),
            Financial("table-house-win", "blackjack", 10m, 8m, 2m, NowUtc.AddMinutes(-2)),
            Financial("table-house-loss", "blackjack", 10m, 15m, -5m, NowUtc.AddMinutes(-1))
        ]));
        var controller = Controller(store, new FakeAuthorizer(AdminOperationsAccessStatus.Authorized), enabled: true);

        var overviewResult = Assert.IsType<OkObjectResult>(await controller.Overview(cancellationToken: CancellationToken.None));
        var overview = Assert.IsType<AdminOperationsOverviewResponse>(overviewResult.Value);
        var activityResult = Assert.IsType<OkObjectResult>(await controller.Activity(cancellationToken: CancellationToken.None));
        var activity = Assert.IsType<AdminOperationsPage<AdminOperationsActivityItem>>(activityResult.Value);

        Assert.Equal(25m, overview.Blackjack.WageredCredits);
        Assert.Equal(27m, overview.Blackjack.PaidCredits);
        Assert.Equal(-2m, overview.Blackjack.HouseNetCredits);
        Assert.Equal(-2m, overview.HouseGamingNetCredits);
        Assert.Contains(activity.Items, item => item.EventId == "table-house-loss" && item.HouseNetCredits == -5m);
    }

    [Fact]
    public async Task Pagination_IsStableOpaqueSignedAndRejectsTampering()
    {
        var financial = Enumerable.Range(0, 3)
            .Select(index => Financial($"event-{index}", "slots", 1m, 0m, 1m, NowUtc.AddMinutes(-index)))
            .ToArray();
        var store = new FakeStore(Snapshot(financial: financial));
        var controller = Controller(store, new FakeAuthorizer(AdminOperationsAccessStatus.Authorized), enabled: true);

        var firstResult = Assert.IsType<OkObjectResult>(await controller.Activity(limit: 2, cancellationToken: CancellationToken.None));
        var first = Assert.IsType<AdminOperationsPage<AdminOperationsActivityItem>>(firstResult.Value);
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.DoesNotContain("event-", first.NextCursor, StringComparison.Ordinal);

        var secondResult = Assert.IsType<OkObjectResult>(await controller.Activity(
            limit: 2,
            cursor: first.NextCursor,
            cancellationToken: CancellationToken.None));
        var second = Assert.IsType<AdminOperationsPage<AdminOperationsActivityItem>>(secondResult.Value);
        Assert.Equal("event-2", Assert.Single(second.Items).EventId);

        var tampered = first.NextCursor![..^1] + (first.NextCursor[^1] == 'A' ? 'B' : 'A');
        var badResult = Assert.IsType<BadRequestObjectResult>(await controller.Activity(
            cursor: tampered,
            cancellationToken: CancellationToken.None));
        Assert.Equal(StatusCodes.Status400BadRequest, badResult.StatusCode);
    }

    [Theory]
    [InlineData("2026-08-01T00:00:00-05:00", "2026-08-02T00:00:00Z", 50)]
    [InlineData("2026-08-02T00:00:00Z", "2026-08-01T00:00:00Z", 50)]
    [InlineData("2026-01-01T00:00:00Z", "2026-08-01T00:00:00Z", 50)]
    [InlineData("2026-08-01T00:00:00Z", "2026-08-02T00:00:00Z", 101)]
    public async Task QueryValidation_RejectsUnsafeRangesAndLimits(string from, string to, int limit)
    {
        var controller = Controller(
            new FakeStore(Snapshot()),
            new FakeAuthorizer(AdminOperationsAccessStatus.Authorized),
            enabled: true);

        var result = Assert.IsType<BadRequestObjectResult>(await controller.Activity(
            DateTimeOffset.Parse(from),
            DateTimeOffset.Parse(to),
            limit,
            cancellationToken: CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [Fact]
    public void PublicDtos_ContainNoSensitiveOrPrivateGameFields()
    {
        var banned = new[]
        {
            "email", "password", "bank", "providerconfig", "sessiontoken", "ipaddress",
            "idempotency", "seed", "deck", "board", "hidden", "rawstate", "userid", "isbot", "skill"
        };
        var dtoTypes = typeof(AdminOperationsOverviewResponse).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(AdminOperationsOverviewResponse).Namespace &&
                           type.IsPublic && type.Name.StartsWith("AdminOperations", StringComparison.Ordinal));
        var names = dtoTypes.SelectMany(type => type.GetProperties()).Select(property => property.Name.ToLowerInvariant()).ToArray();
        Assert.All(banned, word => Assert.DoesNotContain(names, name => name.Contains(word, StringComparison.Ordinal)));
    }

    private static AdminOperationsController Controller(
        FakeStore store,
        FakeAuthorizer authorizer,
        bool enabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:AdminOperationsEnabled"] = enabled.ToString(),
            ["AdminOperations:CursorSigningKey"] = "test-only-admin-cursor-signing-key-0001"
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IAdminOperationsStore>(store)
            .AddSingleton<IAdminOperationsAuthorizer>(authorizer)
            .AddSingleton(Options.Create(new AdminOperationsOptions
            {
                CursorSigningKey = "test-only-admin-cursor-signing-key-0001",
                MaximumRangeDays = 31,
                MaximumDocumentsPerCollection = 100
            }))
            .AddSingleton(Options.Create(new CardBotPlatformOptions()))
            .AddSingleton<AdminOperationsService>()
            .AddSingleton<TimeProvider>(new FixedTimeProvider(NowUtc))
            .BuildServiceProvider();
        var controller = new AdminOperationsController(services)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services }
            }
        };
        return controller;
    }

    private static AdminOperationsSnapshot Snapshot(
        IReadOnlyList<AdminOperationsFinancialRecord>? financial = null,
        IReadOnlyList<AdminOperationsFundingRecord>? funding = null,
        IReadOnlyList<AdminOperationsBotLeaseRecord>? botLeases = null,
        IReadOnlyList<AdminOperationsSourceFinding>? sourceFindings = null) =>
        new(financial ?? [], funding ?? [], [], [], botLeases ?? [], sourceFindings ?? [], true, []);

    private static AdminOperationsFinancialRecord Financial(
        string id,
        string game,
        decimal wagered,
        decimal paid,
        decimal net,
        DateTime? occurredAtUtc = null) =>
        new(id, "gaming", game, "completed", occurredAtUtc ?? NowUtc, wagered, paid, net);

    private sealed class FakeStore(AdminOperationsSnapshot snapshot) : IAdminOperationsStore
    {
        public int Reads { get; private set; }
        public List<(string ActorUserId, string Operation)> Audits { get; } = [];

        public Task<AdminOperationsSnapshot> ReadAsync(AdminOperationsRange range, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(snapshot);
        }

        public Task AppendAuthorizedAccessAuditAsync(
            string actorUserId,
            string operation,
            DateTime occurredAtUtc,
            CancellationToken cancellationToken)
        {
            Audits.Add((actorUserId, operation));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthorizer(AdminOperationsAccessStatus status) : IAdminOperationsAuthorizer
    {
        public int Calls { get; private set; }
        public Task<AdminOperationsAccess> AuthorizeAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(status == AdminOperationsAccessStatus.Authorized
                ? new AdminOperationsAccess(status, "admin-user")
                : new AdminOperationsAccess(status));
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
