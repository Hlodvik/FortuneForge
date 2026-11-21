using System.Net;
using System.Text;
using FortuneForge.Games.ArcaneArchives;
using FortuneForge.Games.ArcaneArchives.Server;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.GameWorkers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

public sealed class ArcanePaidWorkerGatewayTests
{
    [Theory]
    [InlineData("arcane-legacy-prepared-round", "past-round-operation", true)]
    [InlineData("arcane-legacy-prepared-round", null, false)]
    [InlineData("slot-operation-conflict", "past-round-operation", false)]
    public async Task OnlyTheExplicitLegacyReplyCanRouteAKeyedRequestToRecovery(string code, string? operationId, bool recover)
    {
        using var transport = new RecordingTransport(HttpStatusCode.Conflict, "{\"code\":\"" + code + "\"}");
        using var http = new HttpClient(transport);
        var context = new DefaultHttpContext();
        context.Request.Path = ArcanePaidRouteContract.PreparedSpinPath + "/";
        var request = new ArcanePaidSpinCommand(ArcaneArchivesEngine.GameId, 12, false, false);

        var response = await ArcanePaidWorkerGateway.SpinAsync(Client(http), context,
            "account-1", request, operationId, default);

        Assert.Equal(recover, response is null);
        if (response is not null) Assert.Equal(409, response.StatusCode);
        Assert.Equal(context.Request.Path.Value, transport.Path);
        Assert.NotNull(transport.Assertion);
        if (operationId is null)
            Assert.True(Guid.TryParseExact(transport.OperationId, "N", out _));
        else
            Assert.Equal(operationId, transport.OperationId);
    }

    [Fact]
    public async Task AmbiguousFailureDoesNotSendTheRequestToTheEmbeddedGame()
    {
        using var transport = new RecordingTransport(HttpStatusCode.ServiceUnavailable, "{\"code\":\"retry-required\"}");
        using var http = new HttpClient(transport);
        var context = new DefaultHttpContext();
        context.Request.Path = ArcanePaidRouteContract.SpinPath;

        var result = await ArcanePaidWorkerGateway.SpinAsync(Client(http), context, "account-1",
            new(ArcaneArchivesEngine.GameId, 12, false, false), "prepared-operation-id", default);

        Assert.NotNull(result);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task WorkerPauseResponseUsesTheTransportWithoutAutomaticRetries()
    {
        const string body = "{\"code\":\"slot-prepared-start-paused\"}";
        using var standardTransport = new RecordingTransport(HttpStatusCode.OK, "{}");
        using var exactTransport = new RecordingTransport(HttpStatusCode.ServiceUnavailable, body);
        using var http = new HttpClient(standardTransport);
        var context = new DefaultHttpContext();
        context.Request.Path = ArcanePaidRouteContract.PreparedSpinPath;

        var result = await ArcanePaidWorkerGateway.SpinAsync(Client(http, new ExactClientFactory(exactTransport)), context,
            "account-1", new(ArcaneArchivesEngine.GameId, 12, false, false), "paused-operation-id", default);

        Assert.NotNull(result);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal(body, result.Content);
        Assert.Equal(0, standardTransport.Calls);
        Assert.Equal(1, exactTransport.Calls);
    }

    private static GameWorkerClient Client(HttpClient http, IHttpClientFactory? factory = null) => new(http,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"GameWorkers:{ArcaneArchivesEngine.GameId}:Url"] = "http://127.0.0.1:5177",
            [$"GameWorkers:{ArcaneArchivesEngine.GameId}:SigningKey"] = Convert.ToBase64String(new byte[32])
        }).Build(), new DevelopmentHost(), TimeProvider.System, NullLogger<GameWorkerClient>.Instance, factory);

    private sealed class ExactClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(GameWorkerClient.ExactResponseClient, name);
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class RecordingTransport(HttpStatusCode status, string body) : HttpMessageHandler
    {
        internal string? Path { get; private set; }
        internal string? OperationId { get; private set; }
        internal string? Assertion { get; private set; }
        internal int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Path = request.RequestUri?.AbsolutePath;
            OperationId = request.Headers.GetValues("Idempotency-Key").Single();
            Assertion = request.Headers.GetValues(GameWorkerAuthentication.AssertionHeader).Single();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class DevelopmentHost : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "gateway-tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
