using FortuneForge.Server.Admin.GameAnalytics;
using FortuneForge.Server.Admin.Management;
using FortuneForge.Server.Admin.Operations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Server.Tests.RoundAudit;

public sealed class GameAuditAdminContractTests
{
    [Theory]
    [InlineData(false, 0, 503)]
    [InlineData(true, 1, 401)]
    [InlineData(true, 2, 403)]
    public async Task Analytics_private_evidence_and_incident_endpoints_deny_before_resolving_database(bool enabled, int access, int status)
    {
        var services = Services(enabled, (AdminOperationsAccessStatus)access);
        var analytics = new GameAnalyticsController(services) { ControllerContext = Context() };
        var risks = new RiskIncidentsController(services) { ControllerContext = Context() };
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(await analytics.Metrics()).StatusCode);
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(await analytics.Rounds()).StatusCode);
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(await analytics.Evidence("private-round")).StatusCode);
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(await risks.List()).StatusCode);
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(await risks.Create(Draft())).StatusCode);
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(await risks.Resolve("incident", new("upheld", "Review confirmed evidence."))).StatusCode);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("https://foreign.example", "1")]
    [InlineData("https://fortuneforgegame.web.app", null)]
    public async Task Admin_mutations_require_explicit_same_origin_write_even_when_signed_in(string? origin, string? header)
    {
        var controller = new RiskIncidentsController(Services(true, AdminOperationsAccessStatus.Authorized)) { ControllerContext = Context() };
        controller.Request.ContentType = "application/json";
        if (origin is not null) controller.Request.Headers.Origin = origin;
        if (header is not null) controller.Request.Headers[AdminManagementRequestSecurity.Header] = header;
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Create(Draft())).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.Resolve("incident", new("cleared", "Legitimate play."))).StatusCode);
    }

    private static CreateRiskIncidentRequest Draft() => new("alice", "blackjack", "round", "high", "source", "rule", "1", "Attribution basis from retained evidence.", ["evidence"]);
    private static ControllerContext Context() => new() { HttpContext = new DefaultHttpContext() };
    private static ServiceProvider Services(bool enabled, AdminOperationsAccessStatus access)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Features:AdminOperationsEnabled"] = enabled.ToString(), ["Features:AdminManagementEnabled"] = enabled.ToString() }).Build());
        services.AddSingleton<IAdminOperationsAuthorizer>(new Authorizer(access)); services.Configure<AdminManagementOptions>(_ => { });
        return services.BuildServiceProvider();
    }
    private sealed class Authorizer(AdminOperationsAccessStatus status) : IAdminOperationsAuthorizer
    {
        public Task<AdminOperationsAccess> AuthorizeAsync(HttpRequest request, CancellationToken ct) => Task.FromResult(new AdminOperationsAccess(status, "admin"));
    }
}
