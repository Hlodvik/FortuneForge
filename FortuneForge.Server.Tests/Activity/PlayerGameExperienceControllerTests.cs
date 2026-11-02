using FortuneForge.Server.Activity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FortuneForge.Server.Tests.Activity;

public sealed class PlayerGameExperienceControllerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("authenticated-alice")]
    public async Task Endpoint_uses_only_the_authenticated_account_and_never_a_query_user(string? userId)
    {
        var store = new Store();
        using var services = new ServiceCollection().AddSingleton<IGameActivityAccountResolver>(new Accounts(userId))
            .AddSingleton<IPlayerGameExperienceStore>(store).BuildServiceProvider();
        var context = new DefaultHttpContext(); context.Request.QueryString = new("?userId=other-account");
        var controller = new PlayerGameExperienceController(services) { ControllerContext = new() { HttpContext = context } };
        var response = await controller.Get(default);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        if (userId is null) { Assert.IsType<UnauthorizedObjectResult>(response); Assert.Null(store.ReadUser); }
        else
        {
            var body = Assert.IsType<PlayerGameExperienceResponse>(Assert.IsType<OkObjectResult>(response).Value);
            Assert.Equal(userId, body.UserId); Assert.Equal(userId, store.ReadUser); Assert.Equal(45, body.Games.Count);
        }
    }
    private sealed class Accounts(string? userId) : IGameActivityAccountResolver
    { public Task<string?> UserIdAsync(HttpContext context, CancellationToken ct) => Task.FromResult(userId); }
    private sealed class Store : IPlayerGameExperienceStore
    {
        public string? ReadUser;
        public Task<PlayerGameExperienceResponse> ReadAsync(string userId, CancellationToken ct)
        { ReadUser = userId; return Task.FromResult(PlayerGameExperienceProjection.Create(userId, DateTime.UtcNow, [], true)); }
        public Task RecordAcceptedAsync(string userId, AcceptedGameActivity activity, CancellationToken ct) => throw new InvalidOperationException();
    }
}
