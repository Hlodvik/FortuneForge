using System.Text;
using System.IO.Pipelines;
using System.Text.Json;
using FortuneForge.Games.ArcaneArchives.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Activity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.Activity;

public sealed class GameActivityMiddlewareTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Original_response_is_completed_before_blocked_auxiliary_tracking()
    {
        var context = Context();
        var feature = new CompletingResponseBodyFeature();
        context.Features.Set<IHttpResponseBodyFeature>(feature);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Store { OnRecordAsync = async () => { started.SetResult(); await release.Task; } };
        using var recorder = new GameActivityRecorder(store, new Clock());
        var middleware = new GameActivityMiddleware(async ctx =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"roundId\":\"round-one\",\"phase\":\"settled\"}");
        }, NullLogger<GameActivityMiddleware>.Instance);
        var invocation = middleware.InvokeAsync(context, new Accounts(), recorder);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(feature.Completed);
            Assert.False(invocation.IsCompleted);
            Assert.Equal("{\"roundId\":\"round-one\",\"phase\":\"settled\"}", Encoding.UTF8.GetString(feature.Output.ToArray()));
            Assert.Same(feature, context.Features.Get<IHttpResponseBodyFeature>());
        }
        finally { release.TrySetResult(); await invocation; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Successful_game_response_is_delivered_before_tracking_even_when_tracking_fails_or_cancels(bool cancel)
    {
        var context = Context(); var output = (MemoryStream)context.Response.Body;
        var store = new Store
        {
            OnRecord = () =>
            {
                Assert.Equal("{\"roundId\":\"round-one\",\"phase\":\"settled\"}", Encoding.UTF8.GetString(output.ToArray()));
                Assert.Equal(output.Length, context.Response.ContentLength);
                throw cancel ? new OperationCanceledException() : new InvalidOperationException("Activity database unavailable");
            },
        };
        using var recorder = new GameActivityRecorder(store, new Clock());
        var middleware = new GameActivityMiddleware(async ctx =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"roundId\":\"round-one\",\"phase\":\"settled\"}");
        }, NullLogger<GameActivityMiddleware>.Instance);
        await middleware.InvokeAsync(context, new Accounts(), recorder);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Same(output, context.Response.Body);
        Assert.Contains("round-one", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(409)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Rejected_game_commands_never_record_activity(int status)
    {
        var context = Context(); var accounts = new Accounts(); var store = new Store();
        using var recorder = new GameActivityRecorder(store, new Clock());
        var middleware = new GameActivityMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = status;
            await ctx.Response.WriteAsync("{\"roundId\":\"old-round\",\"error\":\"rejected\"}");
        }, NullLogger<GameActivityMiddleware>.Instance);
        await middleware.InvokeAsync(context, accounts, recorder);
        Assert.Empty(store.Writes); Assert.Equal(0, accounts.Calls); Assert.Equal(status, context.Response.StatusCode);
    }

    [Fact]
    public async Task High_frequency_moves_are_throttled_but_results_switches_and_new_runs_are_retained()
    {
        var store = new Store(); var clock = new Clock();
        using var recorder = new GameActivityRecorder(store, clock);
        await recorder.RecordAsync("user", new("2048", "run", "move1"), default);
        await recorder.RecordAsync("user", new("2048", "run", "move2"), default);
        Assert.Single(store.Writes);
        clock.Now = Now.AddSeconds(31);
        await recorder.RecordAsync("user", new("2048", "run", "move3"), default);
        await recorder.RecordAsync("user", new("2048", "run", "final", true), default);
        await recorder.RecordAsync("user", new("2048", "run", "final", true), default);
        Assert.Equal(3, store.Writes.Count);
        await recorder.RecordAsync("user", new("mines", "mine-run", "start"), default);
        await recorder.RecordAsync("user", new("2048", "run", "move4"), default);
        await recorder.RecordAsync("user", new("2048", "new-run", "move1"), default);
        await recorder.RecordAsync("other-user", new("2048", "new-run", "move1"), default);
        Assert.Equal(7, store.Writes.Count);
    }

    [Fact]
    public async Task Failed_tracking_is_not_cached_and_next_valid_action_retries()
    {
        var store = new Store { OnRecord = () => throw new InvalidOperationException() };
        using var recorder = new GameActivityRecorder(store, new Clock());
        await Assert.ThrowsAsync<InvalidOperationException>(() => recorder.RecordAsync("user", new("craps", "round", "roll"), default));
        store.OnRecord = null;
        await recorder.RecordAsync("user", new("craps", "round", "roll"), default);
        Assert.Single(store.Writes);
    }

    [Theory]
    [InlineData(PreparedSlotSpinRoutes.Path, "wukong-journey-v1", "wukong-journey-to-the-west")]
    [InlineData(ArcaneFeatureEndpoints.RoutePrefix + ArcaneFeatureEndpoints.PreparedSpinRoute, "arcane-archives-v1", "arcane-archives")]
    public async Task Prepared_slot_aliases_record_account_scoped_activity_and_share_one_receipt_with_legacy_retries(
        string preparedPath, string runtimeId, string catalogueId)
    {
        var spinId = Guid.NewGuid().ToString();
        var response = JsonSerializer.Serialize(new { gameId = runtimeId, spinId });
        var accounts = new Accounts();
        var store = new Store();
        var experience = new Experience();
        using var recorder = new GameActivityRecorder(store, new Clock());
        var middleware = new GameActivityMiddleware(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(response);
        }, NullLogger<GameActivityMiddleware>.Instance, experience);
        foreach (var path in new[] { preparedPath, "/api/slots/spins" })
        {
            var context = Context();
            context.Request.Path = path;
            context.Request.Headers["Idempotency-Key"] = Guid.NewGuid().ToString("N");
            await middleware.InvokeAsync(context, accounts, recorder);
            Assert.Equal(response, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
        }
        Assert.Equal(2, accounts.Calls);
        var recent = Assert.Single(store.Writes);
        Assert.Equal("verified-account", recent.User);
        Assert.Equal(catalogueId, recent.Activity.GameId);
        Assert.Equal(spinId, recent.Activity.RoundKey);
        Assert.Equal(spinId, recent.Activity.EventKey);
        Assert.True(recent.Activity.IsTerminal);
        Assert.Equal(2, experience.Writes.Count);
        Assert.All(experience.Writes, activity => Assert.Equal(recent, activity));
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext(); context.Request.Method = "POST"; context.Request.Path = "/api/games/baccarat/rounds";
        context.Response.Body = new MemoryStream(); return context;
    }
    private sealed class Accounts : IGameActivityAccountResolver
    {
        public int Calls;
        public Task<string?> UserIdAsync(HttpContext context, CancellationToken ct) { Calls++; return Task.FromResult<string?>("verified-account"); }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTime Now = GameActivityMiddlewareTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Store : IGameActivityStore
    {
        public Action? OnRecord;
        public Func<Task>? OnRecordAsync;
        public List<(string User, AcceptedGameActivity Activity)> Writes = [];
        public async Task RecordAsync(string userId, AcceptedGameActivity activity, DateTime nowUtc, CancellationToken ct)
        { OnRecord?.Invoke(); if (OnRecordAsync is not null) await OnRecordAsync(); Writes.Add((userId, activity)); }
        public Task<IReadOnlyList<RecentGameActivity>> ReadAsync(string userId, CancellationToken ct) => Task.FromResult<IReadOnlyList<RecentGameActivity>>([]);
    }
    private sealed class Experience : IPlayerGameExperienceStore
    {
        public List<(string User, AcceptedGameActivity Activity)> Writes = [];
        public Task RecordAcceptedAsync(string userId, AcceptedGameActivity activity, CancellationToken ct)
        { Writes.Add((userId, activity)); return Task.CompletedTask; }
        public Task<PlayerGameExperienceResponse> ReadAsync(string userId, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class CompletingResponseBodyFeature : IHttpResponseBodyFeature
    {
        public MemoryStream Output { get; } = new();
        public Stream Stream => Output;
        public PipeWriter Writer => PipeWriter.Create(Output, new StreamPipeWriterOptions(leaveOpen: true));
        public bool Completed { get; private set; }
        public void DisableBuffering() { }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CompleteAsync() { Completed = true; return Task.CompletedTask; }
        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
