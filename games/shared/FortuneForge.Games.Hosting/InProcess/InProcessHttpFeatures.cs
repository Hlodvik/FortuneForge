using System.IO.Pipelines;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace FortuneForge.Games.Hosting.InProcess;

internal static class InProcessFeatures
{
    internal static IFeatureCollection Create(HttpRequestMessage message, Uri uri, MemoryStream input,
        InProcessResponse response, CancellationTokenSource abort)
    {
        var headers = new HeaderDictionary();
        foreach (var header in message.Headers)
            headers.Append(header.Key, new StringValues(header.Value.ToArray()));
        if (message.Content is not null)
            foreach (var header in message.Content.Headers)
                headers.Append(header.Key, new StringValues(header.Value.ToArray()));
        if (!headers.ContainsKey("Host")) headers["Host"] = uri.Authority;
        var bodyLimit = new BodyLimitFeature();
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature
        {
            Method = message.Method.Method,
            Scheme = uri.Scheme,
            PathBase = "",
            Path = PathString.FromUriComponent(uri).Value ?? "/",
            QueryString = uri.Query,
            RawTarget = uri.PathAndQuery,
            Protocol = message.Version.Major >= 2 ? "HTTP/" + message.Version.Major : "HTTP/" + message.Version,
            Headers = headers,
            Body = new RequestBody(input, bodyLimit),
        });
        features.Set<IHttpResponseFeature>(response);
        features.Set<IHttpResponseBodyFeature>(response);
        features.Set<IHttpRequestLifetimeFeature>(new LifetimeFeature(abort));
        features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetectionFeature(input.Length > 0));
        features.Set<IHttpBodyControlFeature>(new BodyControlFeature());
        features.Set<IHttpMaxRequestBodySizeFeature>(bodyLimit);
        features.Set<IHttpConnectionFeature>(new ConnectionFeature
        {
            ConnectionId = Guid.NewGuid().ToString("N"), LocalIpAddress = IPAddress.Loopback,
            RemoteIpAddress = IPAddress.Loopback, LocalPort = uri.Port,
        });
        return features;
    }

    private sealed class LifetimeFeature(CancellationTokenSource abort) : IHttpRequestLifetimeFeature
    {
        public CancellationToken RequestAborted { get; set; } = abort.Token;
        public void Abort() => abort.Cancel();
    }

    private sealed class BodyDetectionFeature(bool body) : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => body;
    }

    private sealed class BodyControlFeature : IHttpBodyControlFeature
    {
        public bool AllowSynchronousIO { get; set; } = true;
    }

    private sealed class ConnectionFeature : IHttpConnectionFeature
    {
        public string ConnectionId { get; set; } = "";
        public IPAddress? RemoteIpAddress { get; set; }
        public IPAddress? LocalIpAddress { get; set; }
        public int RemotePort { get; set; }
        public int LocalPort { get; set; }
    }

    private sealed class BodyLimitFeature : IHttpMaxRequestBodySizeFeature
    {
        private long? maximum;
        public bool IsReadOnly { get; private set; }
        public long? MaxRequestBodySize
        {
            get => maximum;
            set
            {
                if (IsReadOnly) throw new InvalidOperationException("The request body has already been read.");
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                maximum = value;
            }
        }
        public void Reading(long length)
        {
            IsReadOnly = true;
            if (maximum is { } limit && length > limit)
                throw new BadHttpRequestException("The request body exceeds the configured limit.", StatusCodes.Status413PayloadTooLarge);
        }
    }

    private sealed class RequestBody(MemoryStream stream, BodyLimitFeature limit) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => stream.Length;
        public override long Position { get => stream.Position; set => stream.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) { limit.Reading(stream.Length); return stream.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { limit.Reading(stream.Length); return stream.Read(buffer); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { limit.Reading(stream.Length); return stream.ReadAsync(buffer, offset, count, token); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        { limit.Reading(stream.Length); return stream.ReadAsync(buffer, token); }
        public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

internal sealed class InProcessResponse : IHttpResponseFeature, IHttpResponseBodyFeature
{
    private readonly object gate = new();
    private readonly MemoryStream capture;
    private readonly Stream output;
    private readonly Stack<(Func<object, Task> Callback, object State)> starting = new();
    private readonly Stack<(Func<object, Task> Callback, object State)> completed = new();
    private readonly AsyncLocal<bool> inStarting = new();
    private IHeaderDictionary headers = new HeaderDictionary();
    private int statusCode = StatusCodes.Status200OK;
    private string? reasonPhrase;
    private Task? startTask;
    private Task? completeTask;
    private PipeWriter? writer;
    private bool callbacksCompleted;

    internal InProcessResponse(MemoryStream capture)
    {
        this.capture = capture;
        output = new ResponseBody(this, capture);
        Body = output;
    }

    public int StatusCode
    {
        get => statusCode;
        set { if (HasStarted) throw new InvalidOperationException("The response has already started."); statusCode = value; }
    }
    public string? ReasonPhrase
    {
        get => reasonPhrase;
        set { if (HasStarted) throw new InvalidOperationException("The response has already started."); reasonPhrase = value; }
    }
    public IHeaderDictionary Headers
    {
        get => headers;
        set { if (HasStarted) throw new InvalidOperationException("The response has already started."); headers = value ?? throw new ArgumentNullException(nameof(value)); }
    }
    public Stream Body { get; set; }
    public bool HasStarted { get; private set; }
    public bool Completed { get; private set; }
    public Stream Stream => output;
    public PipeWriter Writer => writer ??= PipeWriter.Create(output, new StreamPipeWriterOptions(leaveOpen: true));
    public void DisableBuffering() { }

    public void OnStarting(Func<object, Task> callback, object state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (gate)
        {
            if (HasStarted) throw new InvalidOperationException("The response has already started.");
            starting.Push((callback, state));
        }
    }

    public void OnCompleted(Func<object, Task> callback, object state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (gate)
        {
            if (callbacksCompleted) throw new InvalidOperationException("The request has already completed.");
            completed.Push((callback, state));
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource signal;
        lock (gate)
        {
            if (HasStarted || inStarting.Value) return Task.CompletedTask;
            if (startTask is not null) return startTask;
            signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            startTask = signal.Task;
        }
        _ = FireStartingAsync(signal);
        return signal.Task;
    }

    private async Task FireStartingAsync(TaskCompletionSource signal)
    {
        inStarting.Value = true;
        try
        {
            while (true)
            {
                (Func<object, Task> Callback, object State) item;
                lock (gate)
                {
                    if (starting.Count == 0)
                    {
                        HasStarted = true;
                        if (headers is HeaderDictionary dictionary) dictionary.IsReadOnly = true;
                        break;
                    }
                    item = starting.Pop();
                }
                await item.Callback(item.State).ConfigureAwait(false);
            }
            signal.TrySetResult();
        }
        catch (Exception error) { signal.TrySetException(error); }
        finally { inStarting.Value = false; }
    }

    public Task CompleteAsync()
    {
        TaskCompletionSource signal;
        lock (gate)
        {
            if (inStarting.Value) return Task.CompletedTask;
            if (completeTask is not null) return completeTask;
            signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            completeTask = signal.Task;
        }
        _ = FinishAsync(signal);
        return signal.Task;
    }

    private async Task FinishAsync(TaskCompletionSource signal)
    {
        try { await CompleteCoreAsync().ConfigureAwait(false); signal.TrySetResult(); }
        catch (Exception error) { signal.TrySetException(error); }
    }

    private async Task CompleteCoreAsync()
    {
        await StartAsync().ConfigureAwait(false);
        if (writer is not null)
        {
            var result = await writer.FlushAsync().ConfigureAwait(false);
            if (result.IsCanceled) throw new OperationCanceledException("The response body flush was canceled.");
            await writer.CompleteAsync().ConfigureAwait(false);
        }
        Completed = true;
    }

    internal async Task FireCompletedAsync(Action<Exception> report)
    {
        while (true)
        {
            (Func<object, Task> Callback, object State) item;
            lock (gate)
            {
                if (completed.Count == 0) { callbacksCompleted = true; return; }
                item = completed.Pop();
            }
            try { await item.Callback(item.State).ConfigureAwait(false); }
            catch (Exception error) { report(error); }
        }
    }

    internal void Reset(int errorStatus)
    {
        if (HasStarted) throw new InvalidOperationException("A started response cannot be replaced.");
        capture.SetLength(0);
        statusCode = errorStatus;
        reasonPhrase = null;
        headers = new HeaderDictionary();
        starting.Clear();
        startTask = null;
        completeTask = null;
        writer = null;
    }

    public async Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (offset < 0 || offset > file.Length || count < 0 || count > file.Length - offset) throw new ArgumentOutOfRangeException(nameof(offset));
        file.Position = offset;
        var remaining = count ?? file.Length - offset;
        var buffer = new byte[81920];
        while (remaining > 0)
        {
            var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException();
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }

    private sealed class ResponseBody(InProcessResponse owner, MemoryStream stream) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !owner.Completed;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        private void Writable() { if (owner.Completed) throw new InvalidOperationException("The response body has completed."); }
        public override void Write(byte[] buffer, int offset, int count)
        { Writable(); owner.StartAsync().GetAwaiter().GetResult(); stream.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { Writable(); owner.StartAsync().GetAwaiter().GetResult(); stream.Write(buffer); }
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { Writable(); await owner.StartAsync(token).ConfigureAwait(false); await stream.WriteAsync(buffer.AsMemory(offset, count), token).ConfigureAwait(false); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
        { Writable(); await owner.StartAsync(token).ConfigureAwait(false); await stream.WriteAsync(buffer, token).ConfigureAwait(false); }
        public override void Flush() { if (!owner.Completed) owner.StartAsync().GetAwaiter().GetResult(); }
        public override Task FlushAsync(CancellationToken token) => owner.Completed ? Task.CompletedTask : owner.StartAsync(token);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
