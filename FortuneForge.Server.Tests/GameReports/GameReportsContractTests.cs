using System.Text.Json;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.GameReports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FortuneForge.Server.Tests.GameReports;

public sealed class GameReportsContractTests
{
    private const string Key = "game-report-test-request-12345678";
    private static readonly DateTime Now = new(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);
    private static readonly CreateGameReportRequest Request = new("bug", "The cards stopped responding after I pressed Deal.", "/games/andar-bahar");
    private static GameReportsService Service(Store store) => new(store, Options.Create(new AdminOperationsOptions { CursorSigningKey = new string('k', 40) }));

    [Fact]
    public async Task Submission_uses_authenticated_identity_and_server_catalogue_and_returns_only_receipt()
    {
        var store = new Store();
        var controller = PlayerController(store, new AccountAccessContext("actual-player", "player"));
        var response = Assert.IsType<ObjectResult>(await controller.Create(Request, default));
        Assert.Equal(201, response.StatusCode);
        var draft = Assert.Single(store.Drafts);
        Assert.Equal("actual-player", draft.UserId); Assert.Equal("andar-bahar", draft.GameId); Assert.Equal("Andar Bahar", draft.GameName);
        Assert.Equal("/games/andar-bahar", draft.Path);
        var receipt = Assert.IsType<GameReportReceipt>(response.Value);
        var json = JsonSerializer.Serialize(receipt).ToLowerInvariant();
        Assert.DoesNotContain("userid", json); Assert.DoesNotContain("note", json); Assert.DoesNotContain("message", json);
        Assert.Equal("no-store, max-age=0", controller.Response.Headers.CacheControl);
    }

    [Fact]
    public async Task Anonymous_reports_never_reach_persistence()
    {
        var store = new Store(); var controller = PlayerController(store, null);
        Assert.IsType<UnauthorizedObjectResult>(await controller.Create(Request, default)); Assert.Empty(store.Drafts);
    }

    [Theory]
    [InlineData("missing-header")]
    [InlineData("untrusted-origin")]
    [InlineData("cross-site")]
    [InlineData("missing-origin")]
    public async Task Reports_require_explicit_same_site_JSON_intent(string variant)
    {
        var store = new Store(); var controller = PlayerController(store, new("player", "player"));
        if (variant == "missing-header") controller.Request.Headers.Remove("X-FortuneForge-Report");
        if (variant == "untrusted-origin") controller.Request.Headers.Origin = "https://evil.example";
        if (variant == "cross-site") controller.Request.Headers["Sec-Fetch-Site"] = "cross-site";
        if (variant == "missing-origin") controller.Request.Headers.Remove("Origin");
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Create(Request, default)).StatusCode); Assert.Empty(store.Drafts);
    }

    [Theory]
    [InlineData("unknown", "A sufficiently detailed report", "/games/andar-bahar")]
    [InlineData("bug", "short", "/games/andar-bahar")]
    [InlineData("bug", "A sufficiently detailed report", "https://evil.example/games/andar-bahar")]
    [InlineData("bug", "A sufficiently detailed report", "/games/andar-bahar?token=private")]
    [InlineData("bug", "A sufficiently detailed report", "/admin")]
    [InlineData("bug", "A sufficiently detailed\0report", "/games/andar-bahar")]
    public async Task Invalid_or_sensitive_automatic_metadata_is_rejected_before_store(string category, string message, string path)
    {
        var store = new Store(); var controller = PlayerController(store, new("player", "player"));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(new(category, message, path), default)); Assert.Empty(store.Drafts);
    }

    [Fact]
    public async Task Oversized_messages_and_invalid_retry_keys_are_rejected_and_identity_fields_cannot_be_supplied()
    {
        var store = new Store(); var controller = PlayerController(store, new("player", "player"));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(Request with { Message = new string('x', 3001) }, default));
        controller.Request.Headers["Idempotency-Key"] = "short";
        Assert.IsType<BadRequestObjectResult>(await controller.Create(Request, default)); Assert.Empty(store.Drafts);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CreateGameReportRequest>("{\"Category\":\"bug\",\"Message\":\"A report message\",\"Path\":\"/games/andar-bahar\",\"UserId\":\"admin\"}"));
    }

    [Theory]
    [InlineData(1, 401)]
    [InlineData(2, 403)]
    public async Task Every_admin_report_operation_requires_server_resolved_admin(int accessValue, int expected)
    {
        var store = new Store(); var controller = AdminController(store, (AdminOperationsAccessStatus)accessValue);
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(await controller.List()).StatusCode);
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(await controller.Detail(new string('a', 64), default)).StatusCode);
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(await controller.Update(new string('a', 64), new("resolved", 0, "reviewed"), default)).StatusCode);
        Assert.Equal(0, store.Reads); Assert.Empty(store.Changes);
    }

    [Fact]
    public async Task Admin_surface_honours_existing_feature_gate_and_write_origin_security()
    {
        var store = new Store(); var controller = AdminController(store, enabled: false);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.List()).StatusCode);
        controller = AdminController(store); controller.Request.Headers.Origin = "https://evil.example";
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Update(new string('a', 64), new("resolved", 0, "reviewed"), default)).StatusCode);
        Assert.Equal(0, store.Reads); Assert.Empty(store.Changes);
    }

    [Fact]
    public async Task Admin_status_updates_validate_revision_and_record_server_actor()
    {
        var store = new Store(); var controller = AdminController(store);
        Assert.IsType<BadRequestObjectResult>(await controller.Update(new string('a', 64), new("deleted", 0, ""), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Update(new string('a', 64), new("resolved", null, ""), default));
        Assert.Empty(store.Changes);
        Assert.IsType<OkObjectResult>(await controller.Update(new string('a', 64), new("in-review", 0, "Reproduced on phone"), default));
        var change = Assert.Single(store.Changes); Assert.Equal(GameReportsService.Hash("report-admin\nactual-admin"), change.ActorId);
        Assert.Equal("in-review", change.Status); Assert.Equal(0, change.ExpectedVersion);
    }

    [Fact]
    public async Task Pagination_is_bounded_and_uses_a_tamper_checked_operation_scoped_cursor()
    {
        var store = new Store { Rows = [Record(new string('b', 64)), Record(new string('a', 64))] };
        var service = Service(store);
        var first = await service.ListAsync(1, null, default); Assert.NotNull(first.NextCursor); Assert.Single(first.Items);
        await service.ListAsync(1, first.NextCursor, default); Assert.Equal(new string('b', 64), store.BeforeId);
        await Assert.ThrowsAsync<AdminOperationsQueryException>(() => service.ListAsync(1, first.NextCursor + "tampered", default));
        await Assert.ThrowsAsync<GameReportInputException>(() => service.ListAsync(51, null, default));
        await Assert.ThrowsAsync<GameReportInputException>(() => service.GetAsync("../users", default));
    }

    private static GameReportRecord Record(string id) => new(id, "actual-player", "andar-bahar", "Andar Bahar", "/games/andar-bahar", "bug", Request.Message!, "open", 0, Now, Now, "", null);
    private static IServiceProvider Provider(Store store, AccountAccessContext? account, AdminOperationsAccessStatus admin, bool enabled)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Features:AdminOperationsEnabled"] = enabled.ToString() }).Build());
        services.AddSingleton<IGameReportsStore>(store); services.AddSingleton(Service(store));
        services.AddSingleton<IGameReportAccountResolver>(new Resolver(account)); services.AddSingleton<IAdminOperationsAuthorizer>(new Authorizer(admin));
        services.AddSingleton<IAdminOperationsStore>(new Audit()); services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(Options.Create(new GameReportsOptions())); return services.BuildServiceProvider();
    }
    private static T Prepare<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext() };
        controller.Request.Scheme = "https"; controller.Request.Host = new("fortuneforgegame.web.app"); controller.Request.ContentType = "application/json";
        controller.Request.Headers.Origin = "https://fortuneforgegame.web.app"; controller.Request.Headers["X-FortuneForge-Report"] = "1";
        controller.Request.Headers["X-FortuneForge-Admin"] = "1"; controller.Request.Headers["Idempotency-Key"] = Key;
        return controller;
    }
    private static GameReportsController PlayerController(Store store, AccountAccessContext? account) => Prepare(new GameReportsController(Provider(store, account, AdminOperationsAccessStatus.Forbidden, true)));
    private static AdminGameReportsController AdminController(Store store, AdminOperationsAccessStatus admin = AdminOperationsAccessStatus.Authorized, bool enabled = true) => Prepare(new AdminGameReportsController(Provider(store, null, admin, enabled)));
    private sealed class Resolver(AccountAccessContext? account) : IGameReportAccountResolver
    { public Task<AccountAccessContext?> ResolveAsync(HttpRequest request, CancellationToken ct) => Task.FromResult(account); }
    private sealed class Authorizer(AdminOperationsAccessStatus access) : IAdminOperationsAuthorizer
    { public Task<AdminOperationsAccess> AuthorizeAsync(HttpRequest request, CancellationToken ct) => Task.FromResult(new AdminOperationsAccess(access, access == AdminOperationsAccessStatus.Authorized ? "actual-admin" : null)); }
    private sealed class Audit : IAdminOperationsStore
    {
        public Task<AdminOperationsSnapshot> ReadAsync(AdminOperationsRange range, CancellationToken ct) => throw new NotSupportedException();
        public Task AppendAuthorizedAccessAuditAsync(string actor, string operation, DateTime now, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class Store : IGameReportsStore
    {
        public List<GameReportDraft> Drafts { get; } = []; public List<GameReportChange> Changes { get; } = [];
        public IReadOnlyList<GameReportRecord> Rows { get; set; } = []; public int Reads { get; private set; } public string? BeforeId { get; private set; }
        public Task<GameReportRecord> CreateAsync(GameReportDraft draft, CancellationToken ct) { Drafts.Add(draft); return Task.FromResult(Record(draft.Id)); }
        public Task<IReadOnlyList<GameReportRecord>> ListAsync(int limit, DateTime? before, string? id, CancellationToken ct) { Reads++; BeforeId = id; return Task.FromResult(Rows); }
        public Task<GameReportRecord?> GetAsync(string id, CancellationToken ct) { Reads++; return Task.FromResult<GameReportRecord?>(Record(id)); }
        public Task<GameReportRecord?> UpdateAsync(GameReportChange change, CancellationToken ct) { Changes.Add(change); return Task.FromResult<GameReportRecord?>(Record(change.Id) with { Status = change.Status, Version = 1 }); }
    }
}
