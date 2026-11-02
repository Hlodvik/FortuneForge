using System.Text;
using FortuneForge.Server.Activity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Activity;

public sealed class PlayerGameExperienceMiddlewareTests
{
    [Theory]
    [InlineData(200, "alice", true, false)]
    [InlineData(200, "alice", true, true)]
    [InlineData(200, null, false, false)]
    [InlineData(400, "alice", false, false)]
    public async Task Accepted_experience_is_account_scoped_and_does_not_change_popularity(int status, string? user, bool expected, bool fail)
    {
        var context = new DefaultHttpContext(); context.Request.Method = "POST";
        context.Request.Path = "/api/arcade-competitions/flappy/free/runs/run/replay";
        context.Response.Body = new MemoryStream();
        var experience = new Experience { Fail = fail };
        using var recent = new GameActivityRecorder(new Recent(), TimeProvider.System);
        var middleware = new GameActivityMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsync("{\"runId\":\"owned-run\",\"score\":0,\"terminal\":\"collision\"}");
        }, NullLogger<GameActivityMiddleware>.Instance, experience);
        await middleware.InvokeAsync(context, new Accounts(user), recent);
        Assert.Equal(expected, experience.User is not null);
        if (expected) { Assert.Equal(user, experience.User); Assert.Equal("flappy", experience.Activity?.GameId); }
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Contains("owned-run", Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }
    private sealed class Accounts(string? user) : IGameActivityAccountResolver
    { public Task<string?> UserIdAsync(HttpContext context, CancellationToken ct) => Task.FromResult(user); }
    private sealed class Recent : IGameActivityStore
    {
        public Task RecordAsync(string userId, AcceptedGameActivity activity, DateTime nowUtc, CancellationToken ct) => throw new InvalidOperationException("Experience-only receipt must not increment popularity.");
        public Task<IReadOnlyList<RecentGameActivity>> ReadAsync(string userId, CancellationToken ct) => throw new InvalidOperationException();
    }
    private sealed class Experience : IPlayerGameExperienceStore
    {
        public bool Fail;
        public string? User;
        public AcceptedGameActivity? Activity;
        public Task RecordAcceptedAsync(string userId, AcceptedGameActivity activity, CancellationToken ct)
        { User = userId; Activity = activity; if (Fail) throw new InvalidOperationException("Auxiliary store unavailable"); return Task.CompletedTask; }
        public Task<PlayerGameExperienceResponse> ReadAsync(string userId, CancellationToken ct) => throw new InvalidOperationException();
    }
}
