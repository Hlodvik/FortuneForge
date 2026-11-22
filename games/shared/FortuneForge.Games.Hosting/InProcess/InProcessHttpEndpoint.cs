using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FortuneForge.Games.Hosting.InProcess;

/// <summary>A buffered HTTP transport which executes the bound application without a listening socket.</summary>
public sealed class InProcessHttpEndpoint(ILogger<InProcessHttpEndpoint>? logger = null) : IDisposable
{
    private readonly object gate = new();
    private readonly CancellationTokenSource abort = new();
    private readonly ILogger log = logger ?? NullLogger<InProcessHttpEndpoint>.Instance;
    private Application? application;
    private TaskCompletionSource drained = CompletedSource();
    private int active;
    private bool stopped;
    private bool disposed;

    public bool IsBound { get { lock (gate) return application is not null && !stopped; } }

    /// <summary>Binds a platform pipeline while retaining its factory and a fresh service scope for each request.</summary>
    public void Bind(RequestDelegate pipeline, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(services);
        Bind(new DelegateApplication(pipeline, services.GetRequiredService<IHttpContextFactory>(), services));
    }

    internal void Bind<TContext>(IHttpApplication<TContext> value) where TContext : notnull
    {
        ArgumentNullException.ThrowIfNull(value);
        Bind(new HostedApplication<TContext>(value));
    }

    private void Bind(Application value)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (stopped) throw new InvalidOperationException("The in-process endpoint has stopped.");
            if (application is not null) throw new InvalidOperationException("The in-process endpoint is already bound.");
            application = value;
        }
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        Application selected;
        CancellationTokenSource lifetime;
        lock (gate)
        {
            if (stopped || application is null) throw new HttpRequestException("The in-process HTTP endpoint is not accepting requests.");
            selected = application;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, abort.Token);
            if (active++ == 0) drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // IHttpContextAccessor uses a mutable AsyncLocal holder. A nested request must not clear the
        // original holder or inherit an authenticated account/audit context instead of verifying its headers.
        if (ExecutionContext.IsFlowSuppressed()) return Task.Run(RunAsync);
        using (ExecutionContext.SuppressFlow()) return Task.Run(RunAsync);

        async Task<HttpResponseMessage> RunAsync()
        {
            try { return await ExecuteAsync(selected, request, lifetime.Token).ConfigureAwait(false); }
            finally
            {
                lifetime.Dispose();
                lock (gate)
                {
                    if (--active == 0) drained.TrySetResult();
                }
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task pending;
        lock (gate) { stopped = true; pending = drained.Task; }
        try { await pending.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { AbortRequests(); throw; }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            stopped = true;
        }
        AbortRequests();
        // Do not synchronously wait for request callbacks, which may themselves dispose the host.
        _ = DisposeCancellationWhenDrainedAsync();
    }

    private async Task DisposeCancellationWhenDrainedAsync()
    {
        Task pending;
        lock (gate) pending = drained.Task;
        await pending.ConfigureAwait(false);
        abort.Dispose();
    }

    private void AbortRequests()
    {
        try { abort.Cancel(); }
        catch (ObjectDisposedException) { }
        catch (AggregateException error) { log.LogError(error, "An in-process request abort callback failed."); }
    }

    private async Task<HttpResponseMessage> ExecuteAsync(Application selected, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not { IsAbsoluteUri: true } uri || uri.Scheme is not ("http" or "https"))
            throw new HttpRequestException("In-process HTTP requires an absolute HTTP or HTTPS request URI.");
        var bytes = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (request.Content?.Headers.ContentLength is { } length && length != bytes.LongLength)
            throw new HttpRequestException("The HTTP content length differs from the request body.");
        using var input = new MemoryStream(bytes, writable: false);
        using var output = new MemoryStream();
        using var requestAbort = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var response = new InProcessResponse(output);
        var features = InProcessFeatures.Create(request, uri, input, response, requestAbort);
        object? context = null;
        Exception? failure = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            context = await selected.CreateAsync(features).ConfigureAwait(false);
            try
            {
                await selected.ProcessAsync(context).ConfigureAwait(false);
                requestAbort.Token.ThrowIfCancellationRequested();
                await response.CompleteAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                failure = error;
                if (requestAbort.IsCancellationRequested) throw new OperationCanceledException("The in-process HTTP request was aborted.", error, requestAbort.Token);
                if (response.Completed)
                    log.LogError(error, "An in-process application failed after completing its HTTP response.");
                else if (response.HasStarted)
                    throw new HttpRequestException("The in-process application aborted a started HTTP response.", error);
                else
                {
                    log.LogError(error, "An in-process application failed before starting its HTTP response.");
                    response.Reset(error is BadHttpRequestException bad ? bad.StatusCode : StatusCodes.Status500InternalServerError);
                    await response.CompleteAsync().ConfigureAwait(false);
                }
            }
            return CreateResponse(request, response, output);
        }
        catch (Exception error) { failure ??= error; throw; }
        finally
        {
            await response.FireCompletedAsync(error => log.LogError(error, "An in-process response completion callback failed.")).ConfigureAwait(false);
            if (context is not null)
                try { await selected.DisposeAsync(context, failure).ConfigureAwait(false); }
                catch (Exception error) { log.LogError(error, "An in-process request context could not be disposed cleanly."); }
        }
    }

    private static HttpResponseMessage CreateResponse(HttpRequestMessage request, InProcessResponse feature, MemoryStream body)
    {
        var suppressBody = request.Method == HttpMethod.Head || feature.StatusCode is >= 100 and < 200 or 204 or 304;
        var result = new HttpResponseMessage((HttpStatusCode)feature.StatusCode)
        {
            RequestMessage = request,
            Version = request.Version,
            Content = new ByteArrayContent(suppressBody ? [] : body.ToArray()),
        };
        if (feature.ReasonPhrase is { } phrase) result.ReasonPhrase = phrase;
        foreach (var header in feature.Headers)
            if (!result.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                result.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        return result;
    }

    private static TaskCompletionSource CompletedSource()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private abstract class Application
    {
        public abstract ValueTask<object> CreateAsync(IFeatureCollection features);
        public abstract Task ProcessAsync(object context);
        public abstract ValueTask DisposeAsync(object context, Exception? error);
    }

    private sealed class HostedApplication<TContext>(IHttpApplication<TContext> value) : Application where TContext : notnull
    {
        public override ValueTask<object> CreateAsync(IFeatureCollection features) => ValueTask.FromResult<object>(value.CreateContext(features));
        public override Task ProcessAsync(object context) => value.ProcessRequestAsync((TContext)context);
        public override ValueTask DisposeAsync(object context, Exception? error)
        { value.DisposeContext((TContext)context, error); return ValueTask.CompletedTask; }
    }

    private sealed class DelegateApplication(RequestDelegate pipeline, IHttpContextFactory factory, IServiceProvider services) : Application
    {
        private sealed record Context(HttpContext Http, AsyncServiceScope Scope);

        public override ValueTask<object> CreateAsync(IFeatureCollection features)
        {
            var scope = services.CreateAsyncScope();
            try
            {
                var http = factory.Create(features);
                http.RequestServices = scope.ServiceProvider;
                return ValueTask.FromResult<object>(new Context(http, scope));
            }
            catch (Exception error) { return CleanupFailedCreationAsync(scope, error); }
        }

        private static async ValueTask<object> CleanupFailedCreationAsync(AsyncServiceScope scope, Exception error)
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            return await ValueTask.FromException<object>(error).ConfigureAwait(false);
        }

        public override Task ProcessAsync(object context) => pipeline(((Context)context).Http);

        public override async ValueTask DisposeAsync(object context, Exception? error)
        {
            var request = (Context)context;
            try { await request.Scope.DisposeAsync().ConfigureAwait(false); }
            finally { factory.Dispose(request.Http); }
        }
    }
}
