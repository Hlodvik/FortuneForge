using FortuneForge.Server.Admin.Operations;
using FortuneForge.Server.SecurityAudit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Server.Tests.SecurityAudit;

public sealed class InvestigationControllerTests
{
    [Theory]
    [InlineData(false, 0, 503)]
    [InlineData(true, 1, 401)]
    [InlineData(true, 2, 403)]
    public async Task EveryPrivateRouteRequiresEnabledAdminOperationsAndAdministratorBeforeResolvingEvidenceService(bool enabled, int access, int expected)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Features:AdminOperationsEnabled"] = enabled.ToString() }).Build());
        services.AddSingleton<IAdminOperationsAuthorizer>(new Authorizer((AdminOperationsAccessStatus)access));
        var controller = new InvestigationController(services.BuildServiceProvider())
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(await controller.List()).StatusCode);
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(await controller.Read("id", default)).StatusCode);
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(await controller.Instances("id")).StatusCode);
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(await controller.Evidence("id", "audit")).StatusCode);
        Assert.Equal(expected, Assert.IsAssignableFrom<ObjectResult>(await controller.PairAnalysis()).StatusCode);
    }
    private sealed class Authorizer(AdminOperationsAccessStatus status) : IAdminOperationsAuthorizer
    {
        public Task<AdminOperationsAccess> AuthorizeAsync(HttpRequest request, CancellationToken ct) =>
            Task.FromResult(new AdminOperationsAccess(status, status == AdminOperationsAccessStatus.Authorized ? "admin" : null));
    }
}
