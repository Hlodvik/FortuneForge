using System.Security.Cryptography;
using System.Text;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Storage;
using FortuneForge.Server.Games.Hearts;
using FortuneForge.Server.Tests.Solitaire;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FortuneForge.Server.Tests.Games.Hearts;

[Collection(SolitaireFirestoreEmulatorCollection.Name)]
public sealed class HeartsOnboardingFirestoreTests
{
    [Fact]
    public async Task Host_fallback_requires_authentication_and_never_runs_local_game_or_onboarding_policy()
    {
        var database = new FirestoreDbBuilder { ProjectId = $"demo-hearts-host-{Guid.NewGuid():N}", EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        var accounts = new FirestoreAccountStore(database);
        await accounts.CreateAsync("alice", "Alice", "ALICE", "alice@example.test", "unused", "active", default);
        var token = new string('a', 48);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        await accounts.CreateSessionAsync(hash, "alice", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), null, default);
        var context = new DefaultHttpContext();
        var service = new AccountService(accounts, null!, null!, new HttpContextAccessor { HttpContext = context });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Features:HeartsEnabled"] = "true" }).Build();
        var controller = new HeartsOnboardingController(service, configuration)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        Assert.IsType<UnauthorizedObjectResult>(await controller.Get(default));
        Assert.IsType<UnauthorizedObjectResult>(await controller.Confirm(new(true), default));
        Assert.IsType<UnauthorizedObjectResult>(await controller.StartTrial(default));
        Assert.IsType<UnauthorizedObjectResult>(await controller.CancelTrial(Guid.NewGuid(), default));
        context.Request.Headers.Authorization = $"Bearer {token}";
        Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.Get(default)).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.Confirm(new(true), default)).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.StartTrial(default)).StatusCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(await controller.CancelTrial(Guid.NewGuid(), default)).StatusCode);
        Assert.Empty((await database.Collection("heartsAccountOnboarding").GetSnapshotAsync()).Documents);
        Assert.Empty((await database.Collection("heartsFreeSessions").GetSnapshotAsync()).Documents);
    }

    [Fact]
    public async Task Free_and_paid_fallbacks_preserve_feature_and_authentication_gates_without_store_resolution()
    {
        var context = new DefaultHttpContext();
        var service = new AccountService(null!, null!, null!, new HttpContextAccessor { HttpContext = context });
        var disabled = new HeartsController(service, new ConfigurationBuilder().Build())
            { ControllerContext = new ControllerContext { HttpContext = context } };
        Assert.Equal(503, Assert.IsType<ObjectResult>(await disabled.Leave(Guid.NewGuid(), default)).StatusCode);
        var enabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Features:HeartsEnabled"] = "true" }).Build();
        var free = new HeartsController(service, enabled) { ControllerContext = new ControllerContext { HttpContext = context } };
        var paid = new PaidHeartsController(service, enabled) { ControllerContext = new ControllerContext { HttpContext = context } };
        Assert.IsType<UnauthorizedObjectResult>(await free.Leave(Guid.NewGuid(), default));
        Assert.IsType<UnauthorizedObjectResult>(await paid.History(default));
    }

    [Fact]
    public void Api_has_no_user_id_or_trial_completion_input_and_no_reopen_endpoint()
    {
        Assert.Equal(["HasPlayedBefore"], typeof(ConfirmHeartsExperienceRequest).GetProperties().Select(value => value.Name));
        var routes = typeof(HeartsOnboardingController).GetMethods().SelectMany(method => method.GetCustomAttributes(false)
            .OfType<HttpMethodAttribute>()).ToArray();
        Assert.Equal(4, routes.Length);
        Assert.Equal(new[] { "onboarding", "onboarding", "trial", "trial/{matchId:guid}/cancel" }, routes.Select(value => value.Template).Order());
        Assert.DoesNotContain("IsTrial", typeof(StartHeartsMatchRequest).GetProperties().Select(value => value.Name));
    }
}
