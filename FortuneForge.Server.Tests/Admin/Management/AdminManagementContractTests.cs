using System.Text.Json;
using FortuneForge.Games.Abstractions.Analytics;
using FortuneForge.Server.Admin.Management;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.Bots;
using FortuneForge.Server.Tests.Catalog;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.Admin.Management;

public sealed class AdminManagementContractTests
{
    private const string Key = "management-request-0123456789";
    private static readonly AdminManagementOptions Options = new() { CursorSigningKey = new string('k', 40), AllowedOrigins = ["https://fortuneforgegame.web.app"] };
    private static readonly DateTime Now = new(2026, 10, 21, 18, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    public void Provisional_reservation_protocol_requires_explicit_configuration_opt_in(string? configured, bool expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ManagedPlayers:ProvisionalReservationBatchesEnabled"] = configured }).Build();
        var database = new Google.Cloud.Firestore.FirestoreDbBuilder
        { ProjectId = "demo-management-config", Endpoint = "127.0.0.1:8796", ChannelCredentials = Grpc.Core.ChannelCredentials.Insecure,
            EmulatorDetection = Google.Api.Gax.EmulatorDetection.None }.Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(database);
        services.AddManagedPlayers();
        using var provider = services.BuildServiceProvider();
        Assert.Equal(expected, Assert.IsAssignableFrom<IManagedPlayerReservationBatchStore>(
            provider.GetRequiredService<IManagedPlayerAssignmentStore>()).ProvisionalBatchesEnabled);
    }

    [Fact]
    public async Task Default_disabled_gate_never_resolves_authorization_or_settings()
    {
        var store = new Store();
        var auth = new Authorizer(AdminOperationsAccessStatus.Authorized);
        var result = Assert.IsType<ObjectResult>(await Controller(store, auth, false).Get(default));
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(0, auth.Calls);
        Assert.Equal(0, store.Reads);
    }

    [Theory]
    [InlineData(1, 401)]
    [InlineData(2, 403)]
    public async Task Every_read_and_write_requires_a_server_resolved_admin(int accessValue, int status)
    {
        var store = new Store();
        var controller = Controller(store, new Authorizer((AdminOperationsAccessStatus)accessValue));
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(await controller.Get(default)).StatusCode);
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(await controller.GlobalBots(new(false, 0, "Testing a controlled change"), default)).StatusCode);
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(await controller.Audit(cancellationToken: default)).StatusCode);
        Assert.Empty(store.Writes);
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task Cookie_write_requires_custom_header_and_an_explicit_trusted_origin()
    {
        var store = new Store();
        var controller = Controller(store);
        controller.Request.Headers.Remove(AdminManagementRequestSecurity.Header);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Write(controller)).StatusCode);
        controller.Request.Headers[AdminManagementRequestSecurity.Header] = "1";
        controller.Request.Headers.Origin = "https://evil.example";
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Write(controller)).StatusCode);
        controller.Request.Headers.Origin = "null";
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Write(controller)).StatusCode);
        controller.Request.Headers.Remove("Origin");
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Write(controller)).StatusCode);
        controller.Request.Headers.Authorization = "Bearer  ";
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Write(controller)).StatusCode);
        controller.Request.Headers.Remove("Authorization");
        controller.Request.Headers.Origin = "https://fortuneforgegame.web.app";
        controller.Request.Headers["Sec-Fetch-Site"] = "cross-site";
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Write(controller)).StatusCode);
        controller.Request.Headers.Remove("Sec-Fetch-Site");
        Assert.IsType<OkObjectResult>(await Write(controller));
        Assert.Single(store.Writes);
        Assert.Equal("actual-admin", store.Writes[0].ActorUserId);
    }

    [Fact]
    public async Task Explicit_bearer_API_write_without_origin_still_requires_custom_header()
    {
        var controller = Controller(new Store());
        controller.Request.Headers.Remove("Origin");
        controller.Request.Headers.Authorization = "Bearer session-token-from-request";
        Assert.IsType<OkObjectResult>(await Write(controller));
        controller.Request.Headers.Remove(AdminManagementRequestSecurity.Header);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Write(controller)).StatusCode);
    }

    [Theory]
    [InlineData(null, 0, "Valid operational reason", Key)]
    [InlineData(false, -1, "Valid operational reason", Key)]
    [InlineData(false, 0, "short", Key)]
    [InlineData(false, 0, "Contains a\ncontrol character", Key)]
    [InlineData(false, 0, "Valid operational reason", "tiny")]
    [InlineData(false, 0, "Valid operational reason", "invalid/request/key")]
    public async Task Invalid_writes_are_bounded_and_do_not_reach_store(bool? value, long version, string reason, string key)
    {
        var store = new Store();
        var controller = Controller(store);
        controller.Request.Headers["Idempotency-Key"] = key;
        Assert.IsType<BadRequestObjectResult>(await controller.GlobalBots(new(value, version, reason), default));
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Missing_version_unknown_game_and_unsupported_bot_adapter_are_rejected()
    {
        var store = new Store();
        var controller = Controller(store);
        Assert.IsType<BadRequestObjectResult>(await controller.GlobalBots(new(false, null, "Valid operational reason"), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Visibility("../../users", new(false, 0, "Valid operational reason"), default));
        Assert.IsType<BadRequestObjectResult>(await controller.GameBots("house-holdem", new(false, 0, "Valid operational reason"), default));
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Mines_supports_visibility_controls_but_not_bot_admission_controls()
    {
        var store = new Store();
        var controller = Controller(store);
        Assert.IsType<OkObjectResult>(await controller.Visibility("mines", new(false, 0, "Hide Mines while reviewing release readiness"), default));
        var mutation = Assert.Single(store.Writes);
        Assert.Equal("catalog-visibility", mutation.Operation);
        Assert.Equal("mines", mutation.GameId);
        Assert.False(mutation.Value);
        Assert.IsType<BadRequestObjectResult>(await controller.GameBots("mines", new(false, 0, "Mines is a single-player game"), default));
        Assert.Single(store.Writes);

        store.State = new(1, true, new Dictionary<string, bool>(), new Dictionary<string, bool> { ["mines"] = false }, Now);
        var state = await new AdminManagementService(store, Microsoft.Extensions.Options.Options.Create(Options)).GetAsync(default);
        var game = Assert.Single(state.Games, item => item.Id == "mines");
        Assert.False(game.Visible);
        Assert.False(game.BotsSupported);
        Assert.Equal("/games/mines", game.Route);
    }

    [Fact]
    public async Task Global_off_does_not_overwrite_independent_game_switches_or_catalogue_visibility()
    {
        var service = new AdminManagementService(new Store { State = new(4, false,
            new Dictionary<string, bool> { ["hearts"] = false }, new Dictionary<string, bool> { ["baccarat"] = false }, Now) }, Microsoft.Extensions.Options.Options.Create(Options));
        var state = await service.GetAsync(default);
        Assert.False(state.BotsEnabled);
        Assert.False(state.Games.Single(game => game.Id == "hearts").BotsEnabled);
        Assert.True(state.Games.Single(game => game.Id == "blackjack").BotsEnabled);
        Assert.All(state.Games, game => Assert.False(game.EffectiveBotsEnabled));
        Assert.False(state.Games.Single(game => game.Id == "baccarat").Visible);
        Assert.False(state.Games.Single(game => game.Id == "horse-flight").Visible);
    }

    [Fact]
    public async Task Public_visibility_contains_only_allowlisted_routes_and_visibility_not_runtime_or_identity_data()
    {
        var service = new AdminManagementService(new Store(), Microsoft.Extensions.Options.Options.Create(Options));
        var publicState = await service.VisibilityAsync(default);
        Assert.Equal(CatalogContract.Entries,
            publicState.Games.Select(game => (game.Id, game.Route)).OrderBy(game => game.Id, StringComparer.Ordinal).ToArray());
        Assert.Equal(CatalogContract.Entries.Count, publicState.Games.Select(game => game.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(CatalogContract.Entries.Count, publicState.Games.Select(game => game.Route).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(CatalogContract.DefaultHiddenIds,
            publicState.Games.Where(game => !game.Visible).Select(game => game.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.Equal(CatalogContract.DefaultVisibleIds,
            publicState.Games.Where(game => game.Visible).Select(game => game.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.True(publicState.Games.Single(game => game.Id == "2048").Visible);
        Assert.True(publicState.Games.Single(game => game.Id == "drop-merge").Visible);
        Assert.True(publicState.Games.Single(game => game.Id == "dragon-tiger").Visible);
        Assert.True(publicState.Games.Single(game => game.Id == "hi-lo").Visible);
        Assert.True(publicState.Games.Single(game => game.Id == "andar-bahar").Visible);
        Assert.True(publicState.Games.Single(game => game.Id == "mines").Visible);
        var json = JsonSerializer.Serialize(publicState).ToLowerInvariant();
        Assert.DoesNotContain("bots", json);
        Assert.DoesNotContain("actor", json);
        Assert.DoesNotContain("reason", json);
    }

    [Fact]
    public void Release_catalogue_preserves_title_identity_and_distinguishes_visibility_from_analytics_coverage()
    {
        Assert.Equal(CatalogContract.Entries,
            AdminManagementCatalog.Games.Select(game => (game.Id, game.Route)).OrderBy(game => game.Id, StringComparer.Ordinal).ToArray());
        var competitive = AdminManagementCatalog.Require("solitaire");
        var free = AdminManagementCatalog.Require("free-solitaire");
        var mines = AdminManagementCatalog.Require("mines");
        Assert.Equal(("Competitive Solitaire", "cards", "/cards/solitaire", true, true),
            (competitive.DisplayName, competitive.Category, competitive.Route, competitive.DefaultVisible, competitive.BotsSupported));
        Assert.Equal(("Solitaire", "cards", "/cards/free-solitaire", true, false),
            (free.DisplayName, free.Category, free.Route, free.DefaultVisible, free.BotsSupported));
        Assert.Equal(("Mines", "casino", "/games/mines", true, false),
            (mines.DisplayName, mines.Category, mines.Route, mines.DefaultVisible, mines.BotsSupported));
        Assert.Equal(CatalogContract.AnalyticsIds, GameAnalyticsOutbox.GameIds.OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.Equal(CatalogContract.UntrackedArcadeIds,
            CatalogContract.Ids.Except(GameAnalyticsOutbox.GameIds, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "liars-dice" },
            GameAnalyticsOutbox.GameIds.Except(CatalogContract.DefaultVisibleIds, StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Runtime_visibility_overrides_change_only_allowlisted_visibility_not_release_identity_or_defaults()
    {
        var store = new Store { State = new(7, true, new Dictionary<string, bool>(), new Dictionary<string, bool>
        {
            ["free-solitaire"] = false, ["mines"] = false, ["asteroids"] = true, ["liars-dice"] = true, ["unregistered"] = true,
        }, Now) };
        var service = new AdminManagementService(store, Microsoft.Extensions.Options.Options.Create(Options));
        var publicState = await service.VisibilityAsync(default);
        Assert.Equal(7, publicState.Version);
        Assert.Equal(CatalogContract.Entries,
            publicState.Games.Select(game => (game.Id, game.Route)).OrderBy(game => game.Id, StringComparer.Ordinal).ToArray());
        var expectedVisible = CatalogContract.DefaultVisibleIds.Except(new[] { "free-solitaire", "mines" }, StringComparer.Ordinal)
            .Concat(new[] { "asteroids", "liars-dice" }).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedVisible,
            publicState.Games.Where(game => game.Visible).Select(game => game.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(publicState.Games, game => game.Id == "unregistered");
        Assert.Equal(CatalogContract.DefaultVisibleIds,
            AdminManagementCatalog.Games.Where(game => game.DefaultVisible).Select(game => game.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Audit_limits_and_signed_cursor_are_validated_before_store_access()
    {
        var store = new Store();
        var controller = Controller(store);
        Assert.IsType<BadRequestObjectResult>(await controller.Audit(101));
        Assert.IsType<BadRequestObjectResult>(await controller.Audit(0));
        Assert.IsType<BadRequestObjectResult>(await controller.Audit(25, "made-up-cursor"));
        Assert.Equal(0, store.AuditReads);
    }

    [Fact]
    public void Supply_unavailable_is_a_safe_503_not_an_unhandled_new_table_failure()
    {
        var context = new ExceptionContext(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        { Exception = new ManagedPlayerSupplyUnavailableException() };
        new ManagedPlayerSupplyExceptionFilter().OnException(context);
        Assert.True(context.ExceptionHandled);
        Assert.Equal(503, Assert.IsType<ObjectResult>(context.Result).StatusCode);
        Assert.Contains("Existing games can continue", JsonSerializer.Serialize(((ObjectResult)context.Result).Value));
    }

    private static Task<IActionResult> Write(AdminManagementController controller) => controller.GlobalBots(new(false, 0, "Pause new bot opponents for testing"), default);
    private static AdminManagementController Controller(Store store, Authorizer? authorizer = null, bool enabled = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Features:AdminManagementEnabled"] = enabled.ToString() }).Build());
        services.AddSingleton<IAdminOperationsAuthorizer>(authorizer ?? new(AdminOperationsAccessStatus.Authorized));
        services.AddSingleton<IAdminManagementStore>(store);
        services.AddSingleton<IOptions<AdminManagementOptions>>(Microsoft.Extensions.Options.Options.Create(Options));
        services.AddSingleton<AdminManagementService>();
        services.AddSingleton(TimeProvider.System);
        var controller = new AdminManagementController(services.BuildServiceProvider()) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.Request.Scheme = "https";
        controller.Request.Host = new("admin.example");
        controller.Request.ContentType = "application/json";
        controller.Request.Headers.Origin = "https://admin.example";
        controller.Request.Headers[AdminManagementRequestSecurity.Header] = "1";
        controller.Request.Headers["Idempotency-Key"] = Key;
        return controller;
    }
    private sealed class Authorizer(AdminOperationsAccessStatus status) : IAdminOperationsAuthorizer
    {
        public int Calls { get; private set; }
        public Task<AdminOperationsAccess> AuthorizeAsync(HttpRequest request, CancellationToken cancellationToken) { Calls++; return Task.FromResult(new AdminOperationsAccess(status, status == AdminOperationsAccessStatus.Authorized ? "actual-admin" : null)); }
    }
    private sealed class Store : IAdminManagementStore
    {
        public AdminRuntimeState State { get; set; } = AdminRuntimeState.Default;
        public int Reads { get; private set; }
        public int AuditReads { get; private set; }
        public List<AdminManagementMutation> Writes { get; } = [];
        public Task<AdminRuntimeState> ReadAsync(CancellationToken cancellationToken) { Reads++; return Task.FromResult(State); }
        public Task<AdminManagementResponse> MutateAsync(AdminManagementMutation mutation, CancellationToken cancellationToken) { Writes.Add(mutation); return Task.FromResult(State.Response()); }
        public Task<IReadOnlyList<AdminManagementAuditItem>> AuditAsync(int limit, long? beforeVersion, CancellationToken cancellationToken) { AuditReads++; return Task.FromResult<IReadOnlyList<AdminManagementAuditItem>>([]); }
    }
}
