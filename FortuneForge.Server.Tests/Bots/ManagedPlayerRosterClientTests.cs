using System.Net;
using System.Text.Json;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FortuneForge.Server.Tests.Bots;

public sealed class ManagedPlayerRosterClientTests
{
    private const string Game = "liars-dice";
    private const string Assignment = "liars-dice:test-assignment";
    private static readonly DateTime Now = new(2026, 11, 21, 3, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(index => (byte)index).ToArray();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SignedRenewalReturnsOnlyAConfirmedBoolean(bool confirmed)
    {
        using var handler = new RenewalHandler(HttpStatusCode.OK, confirmed ? "true" : "false", includeDigest: true);
        using var services = Services(handler);
        var roster = services.GetRequiredService<IManagedPlayerRosterClient>();

        Assert.Equal(confirmed, await roster.TryRenewAsync(Assignment, ["managed-one", "managed-two"], Now, default));
    }

    [Theory]
    [InlineData(503, "false", true)]
    [InlineData(200, "\"false\"", true)]
    [InlineData(200, "null", true)]
    [InlineData(200, "false", false)]
    public async Task UnavailableOrMalformedRenewalCannotMasqueradeAsALostLease(int status, string json, bool includeDigest)
    {
        using var handler = new RenewalHandler((HttpStatusCode)status, json, includeDigest);
        using var services = Services(handler);
        var roster = services.GetRequiredService<IManagedPlayerRosterClient>();

        await Assert.ThrowsAsync<ManagedPlayerRosterUnavailableException>(() =>
            roster.TryRenewAsync(Assignment, ["managed-one", "managed-two"], Now, default));
    }

    [Fact]
    public async Task BrokenTransportDoesNotConfirmALostLease()
    {
        using var handler = new UnavailableHandler();
        using var services = Services(handler);
        var roster = services.GetRequiredService<IManagedPlayerRosterClient>();

        await Assert.ThrowsAsync<ManagedPlayerRosterUnavailableException>(() =>
            roster.TryRenewAsync(Assignment, ["managed-one", "managed-two"], Now, default));
    }

    private static ServiceProvider Services(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ManagedPlayerRoster:Url"] = "https://managed-roster.example/",
            ["ManagedPlayerRoster:SigningKey"] = Convert.ToBase64String(Key)
        }).Build());
        services.AddSingleton<IHostEnvironment, HostEnvironment>();
        services.AddSingleton<TimeProvider>(new FixedClock());
        services.AddManagedPlayerRosterClient(Game);
        services.AddSingleton<IHttpClientFactory>(new HandlerClientFactory(handler));
        return services.BuildServiceProvider();
    }

    private sealed class RenewalHandler(HttpStatusCode code, string json, bool includeDigest) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            var expectedPath = GameManagedPlayerRosterProtocol.Route(Game, GameManagedPlayerRosterProtocol.TryRenewAction);
            Assert.Equal(expectedPath, request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            var terms = Assert.IsType<ManagedPlayerLeaseRequest>(
                JsonSerializer.Deserialize<ManagedPlayerLeaseRequest>(body, GameManagedPlayerRosterProtocol.WireJson));
            Assert.Equal(Assignment, terms.AssignmentId);
            Assert.Equal(Now, terms.NowUtc);
            Assert.Equal(["managed-one", "managed-two"], terms.ProfileIds);

            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = expectedPath;
            context.Request.ContentLength = body.Length;
            await using var stream = new MemoryStream(body);
            context.Request.Body = stream;
            context.Request.Headers[GameManagedPlayerRosterProtocol.AssertionHeader] =
                Assert.Single(request.Headers.GetValues(GameManagedPlayerRosterProtocol.AssertionHeader));
            var proof = await GameManagedPlayerRosterProtocol.VerifyRequestAsync(context.Request, Game, Key,
                new DateTimeOffset(Now), cancellationToken) ?? throw new InvalidOperationException("The roster assertion did not verify.");
            Assert.Equal(Assignment, proof.AccountId);

            var response = new HttpResponseMessage(code) { Content = new StringContent(json) };
            if (includeDigest)
                response.Headers.Add(GameManagedPlayerRosterProtocol.RequestSha256Header, proof.BodySha256);
            return response;
        }
    }

    private sealed class UnavailableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Simulated connection loss."));
    }

    private sealed class HandlerClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class HostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "ManagedPlayerRosterClientTests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
