using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FortuneForge.Games.Hosting.InProcess;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

public sealed class InProcessHttpEndpointTests
{
    private static readonly AsyncLocal<string?> Ambient = new();

    [Fact]
    public async Task Buffered_transport_preserves_the_HTTP_contract_and_does_not_invent_authentication()
    {
        await using var services = Services();
        using var endpoint = new InProcessHttpEndpoint();
        byte[] sent = [0, 128, 255, 1], returned = [255, 0, 127, 42];
        endpoint.Bind(async context =>
        {
            Assert.Equal("PATCH", context.Request.Method);
            Assert.Equal("https", context.Request.Scheme);
            Assert.Equal("worker.internal:8443", context.Request.Host.Value);
            Assert.Equal("/api/round one/thing%2Ftwo", context.Request.Path.Value);
            Assert.Equal("?name=a%2Fb&plus=a+b", context.Request.QueryString.Value);
            Assert.Equal("/api/round%20one/thing%2Ftwo?name=a%2Fb&plus=a+b", context.Features.Get<IHttpRequestFeature>()!.RawTarget);
            Assert.Equal("application/octet-stream", context.Request.ContentType);
            Assert.Equal(new[] { "first", "second" }, context.Request.Headers["X-Test"].ToArray());
            Assert.Equal("signed-assertion", context.Request.Headers["X-Worker-Assertion"].ToString());
            Assert.False(context.User.Identity?.IsAuthenticated ?? false);
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body);
            Assert.Equal(sent, body.ToArray());
            context.Response.StatusCode = 207;
            context.Features.Get<IHttpResponseFeature>()!.ReasonPhrase = "Exact result";
            context.Response.ContentType = "application/octet-stream";
            context.Response.ContentLength = returned.Length;
            context.Response.Headers["X-Result"] = new StringValues(["one", "two"]);
            context.Response.Headers.SetCookie = new StringValues(["a=1; Path=/", "b=2; HttpOnly"]);
            await context.Response.Body.WriteAsync(returned);
        }, services);
        using var client = new HttpClient(new InProcessHttpMessageHandler(endpoint));
        using var request = new HttpRequestMessage(HttpMethod.Patch,
            "https://worker.internal:8443/api/round%20one/thing%2Ftwo?name=a%2Fb&plus=a+b")
        { Content = new ByteArrayContent(sent) };
        request.Content.Headers.ContentType = new("application/octet-stream");
        request.Headers.TryAddWithoutValidation("X-Test", new[] { "first", "second" });
        request.Headers.TryAddWithoutValidation("X-Worker-Assertion", "signed-assertion");
        using var response = await client.SendAsync(request);
        Assert.Equal(207, (int)response.StatusCode);
        Assert.Equal("Exact result", response.ReasonPhrase);
        Assert.Equal(returned, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal((long?)returned.Length, response.Content.Headers.ContentLength);
        Assert.Equal(new[] { "one", "two" }, response.Headers.GetValues("X-Result"));
        Assert.Equal(new[] { "a=1; Path=/", "b=2; HttpOnly" }, response.Headers.GetValues("Set-Cookie"));
    }

    [Fact]
    public async Task Starting_and_completion_callbacks_run_in_reverse_order_before_the_request_scope_is_disposed()
    {
        var probes = new ConcurrentBag<Probe>();
        await using var services = Services(probes);
        using var endpoint = new InProcessHttpEndpoint();
        var events = new List<string>();
        var scopeAliveDuringCompletion = false;
        endpoint.Bind(async context =>
        {
            var probe = context.RequestServices.GetRequiredService<Probe>();
            Assert.Same(context, context.RequestServices.GetRequiredService<IHttpContextAccessor>().HttpContext);
            context.Response.OnStarting(() => { events.Add("starting-first"); context.Response.Headers["X-Starting"] = "done"; return Task.CompletedTask; });
            context.Response.OnStarting(() => { events.Add("starting-second"); return Task.CompletedTask; });
            context.Response.OnCompleted(() => { events.Add("completed-first"); scopeAliveDuringCompletion = !probe.Disposed; return Task.CompletedTask; });
            context.Response.OnCompleted(() => { events.Add("completed-second"); return Task.CompletedTask; });
            await context.Response.BodyWriter.WriteAsync(Encoding.UTF8.GetBytes("body"));
        }, services);
        using var response = await endpoint.SendAsync(new(HttpMethod.Get, "https://platform.internal/test"));
        Assert.Equal("body", await response.Content.ReadAsStringAsync());
        Assert.Equal("done", response.Headers.GetValues("X-Starting").Single());
        Assert.Equal(new[] { "starting-second", "starting-first", "completed-second", "completed-first" }, events);
        Assert.True(scopeAliveDuringCompletion);
        Assert.True(Assert.Single(probes).Disposed);
    }

    [Fact]
    public async Task Custom_server_uses_the_real_ASPNET_application_factory_and_closes_scopes()
    {
        var probes = new ConcurrentBag<Probe>();
        using var server = new InProcessGameServer();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<IServer>(server);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped(_ => NewProbe(probes));
        await using var app = builder.Build();
        app.Run(async context =>
        {
            var probe = context.RequestServices.GetRequiredService<Probe>();
            Assert.Same(context, context.RequestServices.GetRequiredService<IHttpContextAccessor>().HttpContext);
            await context.Response.WriteAsync(probe.Id.ToString());
        });
        await app.StartAsync();
        Assert.NotNull(server.Features.Get<IServerAddressesFeature>());
        using (var response = await server.Endpoint.SendAsync(new(HttpMethod.Get, "https://game.internal/run")))
            Assert.Equal(Assert.Single(probes).Id.ToString(), await response.Content.ReadAsStringAsync());
        Assert.True(Assert.Single(probes).Disposed);
        await app.StopAsync();
        Assert.False(server.Endpoint.IsBound);
        await Assert.ThrowsAsync<HttpRequestException>(() => server.Endpoint.SendAsync(new(HttpMethod.Get, "https://game.internal/run")));
    }

    [Fact]
    public async Task Parallel_requests_have_isolated_scopes_and_cannot_clear_the_callers_ambient_context()
    {
        const int count = 12;
        var probes = new ConcurrentBag<Probe>();
        await using var services = Services(probes);
        using var endpoint = new InProcessHttpEndpoint();
        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var original = accessor.HttpContext;
        var originalAmbient = Ambient.Value;
        var outer = new DefaultHttpContext();
        accessor.HttpContext = outer;
        Ambient.Value = "outer";
        var entered = 0;
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        endpoint.Bind(async context =>
        {
            Assert.Null(Ambient.Value);
            Assert.Same(context, accessor.HttpContext);
            var id = context.Request.Headers["X-Request"].ToString();
            Ambient.Value = id;
            var probe = context.RequestServices.GetRequiredService<Probe>();
            if (Interlocked.Increment(ref entered) == count) allEntered.TrySetResult();
            await allEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Yield();
            Assert.Equal(id, Ambient.Value);
            Assert.Same(context, accessor.HttpContext);
            await context.Response.WriteAsync(id + ":" + probe.Id);
        }, services);
        try
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, count).Select(async index =>
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://game.internal/parallel");
                request.Headers.Add("X-Request", index.ToString());
                using var response = await endpoint.SendAsync(request);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                return await response.Content.ReadAsStringAsync();
            }));
            Assert.Equal(count, responses.Distinct().Count());
            for (var index = 0; index < count; index++) Assert.StartsWith(index + ":", responses[index]);
            Assert.Equal(count, probes.Count);
            Assert.Equal(count, probes.Select(probe => probe.Id).Distinct().Count());
            Assert.All(probes, probe => Assert.True(probe.Disposed));
            Assert.Equal("outer", Ambient.Value);
            Assert.Same(outer, accessor.HttpContext);
        }
        finally { accessor.HttpContext = original; Ambient.Value = originalAmbient; }
    }

    [Fact]
    public async Task Caller_cancellation_aborts_the_application_and_still_disposes_its_scope()
    {
        var probes = new ConcurrentBag<Probe>();
        await using var services = Services(probes);
        using var endpoint = new InProcessHttpEndpoint();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        endpoint.Bind(async context =>
        {
            _ = context.RequestServices.GetRequiredService<Probe>();
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
        }, services);
        using var cancellation = new CancellationTokenSource();
        var request = endpoint.SendAsync(new(HttpMethod.Get, "https://game.internal/wait"), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.True(Assert.Single(probes).Disposed);
    }

    [Fact]
    public async Task Stop_drains_existing_requests_and_rejects_new_requests_without_blocking_startup()
    {
        await using var services = Services();
        using var endpoint = new InProcessHttpEndpoint();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        endpoint.Bind(async context => { entered.TrySetResult(); await release.Task; await context.Response.WriteAsync("finished"); }, services);
        var request = endpoint.SendAsync(new(HttpMethod.Get, "https://game.internal/wait"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = endpoint.StopAsync();
        Assert.False(stop.IsCompleted);
        await Assert.ThrowsAsync<HttpRequestException>(() => endpoint.SendAsync(new(HttpMethod.Get, "https://game.internal/new")));
        release.TrySetResult();
        using var response = await request;
        await stop;
        Assert.Equal("finished", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Stop_timeout_aborts_inflight_requests_instead_of_waiting_forever()
    {
        await using var services = Services();
        using var endpoint = new InProcessHttpEndpoint();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        endpoint.Bind(async context => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }, services);
        var request = endpoint.SendAsync(new(HttpMethod.Get, "https://game.internal/wait"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var stop = endpoint.StopAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stop);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Theory]
    [InlineData(false, 500)]
    [InlineData(true, 413)]
    public async Task Unhandled_errors_before_start_have_server_error_status_without_leaking_exception_details(bool badRequest, int expected)
    {
        await using var services = Services();
        using var endpoint = new InProcessHttpEndpoint();
        endpoint.Bind(_ =>
        {
            if (badRequest) throw new BadHttpRequestException("private detail", 413);
            throw new InvalidOperationException("private detail");
        }, services);
        using var response = await endpoint.SendAsync(new(HttpMethod.Get, "https://game.internal/error"));
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Completed_rejection_keeps_its_deliberate_status_even_if_auxiliary_completion_fails()
    {
        await using var services = Services();
        using var endpoint = new InProcessHttpEndpoint();
        endpoint.Bind(async context =>
        {
            context.Response.StatusCode = 403;
            context.Response.OnCompleted(() => throw new InvalidOperationException("auxiliary failure"));
            await context.Response.WriteAsync("denied");
            await context.Response.CompleteAsync();
            throw new InvalidOperationException("after completed response");
        }, services);
        using var response = await endpoint.SendAsync(new(HttpMethod.Get, "https://game.internal/reject"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("denied", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_exception_after_partial_output_is_an_aborted_transport_not_a_successful_partial_paid_result()
    {
        await using var services = Services();
        using var endpoint = new InProcessHttpEndpoint();
        endpoint.Bind(async context => { await context.Response.WriteAsync("partial"); throw new InvalidOperationException("failure"); }, services);
        await Assert.ThrowsAsync<HttpRequestException>(() => endpoint.SendAsync(new(HttpMethod.Get, "https://game.internal/partial")));
    }

    [Fact]
    public async Task Body_limits_set_by_authentication_are_enforced_when_the_body_is_read()
    {
        await using var services = Services();
        using var endpoint = new InProcessHttpEndpoint();
        endpoint.Bind(async context =>
        {
            context.Features.Get<IHttpMaxRequestBodySizeFeature>()!.MaxRequestBodySize = 1;
            await context.Request.Body.CopyToAsync(Stream.Null);
        }, services);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://game.internal/body") { Content = new ByteArrayContent([1, 2]) };
        using var response = await endpoint.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    private static ServiceProvider Services(ConcurrentBag<Probe>? probes = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddHttpContextAccessor();
        services.AddSingleton<IHttpContextFactory, DefaultHttpContextFactory>();
        services.AddScoped(_ => NewProbe(probes));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static Probe NewProbe(ConcurrentBag<Probe>? probes)
    {
        var probe = new Probe();
        probes?.Add(probe);
        return probe;
    }

    private sealed class Probe : IAsyncDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
