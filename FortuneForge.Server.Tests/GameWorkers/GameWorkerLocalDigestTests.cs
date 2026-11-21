using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FortuneForge.Server.Tests.GameWorkers;

public sealed class GameWorkerLocalDigestTests
{
    private const string CommandPath = "/command";
    private const string GameId = "digest-fixture";
    private static readonly DateTimeOffset ExpectedTime = new(2026, 11, 21, 1, 20, 30, TimeSpan.Zero);

    [Fact]
    public async Task Loopback_hashes_original_bytes_and_leaves_the_json_body_for_binding()
    {
        await using var app = Build("emulator");
        app.UseLocalGameWorkerCommandDigests(IsFixtureLoopback);
        MapEndpoints(app);
        await app.StartAsync();
        using var client = Client(app);

        var empty = await PostAsync(client, "");
        var emptyObject = await PostAsync(client, "{}");
        Assert.Equal(Hash(""), empty.CommandSha256);
        Assert.Equal(Hash("{}"), emptyObject.CommandSha256);
        Assert.Null(empty.Value);
        Assert.Null(emptyObject.Value);
        Assert.Null(empty.SignedSha256);
        Assert.Null(emptyObject.SignedSha256);

        var original = "{\"value\":\"same\",\"at\":\"2026-11-21T01:20:30Z\"}";
        var spellings = new[]
        {
            original,
            " \r\n{ \"value\" : \"same\", \"at\" : \"2026-11-21T01:20:30Z\" }\t",
            "{\"VaLuE\":\"same\",\"AT\":\"2026-11-21T01:20:30Z\"}",
            "{\"value\":\"same\",\"at\":\"2026-11-21T03:20:30+02:00\"}",
        };
        var digests = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in spellings)
        {
            var reply = await PostAsync(client, raw);
            Assert.Equal(Hash(raw), reply.CommandSha256);
            Assert.True(digests.Add(reply.CommandSha256!));
            Assert.Null(reply.SignedSha256);
            Assert.Equal("same", reply.Value);
            Assert.Equal(ExpectedTime, reply.At);
        }

        const string whitespace = " \t\r\n ";
        using var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, "/raw") { Content = Content(whitespace, "text/plain") };
        using var deleteResponse = await client.SendAsync(deleteRequest);
        var deleted = await ReadAsync(deleteResponse);
        Assert.Equal(Hash(whitespace), deleted.CommandSha256);
        Assert.Equal(whitespace, deleted.Value);
        Assert.Null(deleted.SignedSha256);

        using var getResponse = await client.GetAsync("/raw");
        Assert.Null((await ReadAsync(getResponse)).CommandSha256);

        using var anotherHost = new HttpRequestMessage(HttpMethod.Post, CommandPath) { Content = Content(original) };
        anotherHost.Headers.Host = "other.invalid";
        using var anotherResponse = await client.SendAsync(anotherHost);
        var rejectedByLoopback = await ReadAsync(anotherResponse);
        Assert.Null(rejectedByLoopback.CommandSha256);
        Assert.Equal("same", rejectedByLoopback.Value);
    }

    [Fact]
    public async Task Hosted_loopback_requires_an_assertion_and_exposes_only_the_verified_digest()
    {
        await using var app = Build("hosted");
        var key = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        app.Configuration["GameWorker:SigningKey"] = Convert.ToBase64String(key);
        Assert.Throws<InvalidOperationException>(() => app.UseLocalGameWorkerCommandDigests(IsFixtureLoopback));
        app.UseGameWorkerAuthentication(GameId);
        MapEndpoints(app);
        await app.StartAsync();
        using var client = Client(app);

        const string raw = " { \"VaLuE\" : \"signed\", \"at\" : \"2026-11-21T01:20:30+00:00\" } ";
        using var unsignedBody = Content(raw);
        using var unsigned = await client.PostAsync(CommandPath, unsignedBody);
        Assert.Equal(HttpStatusCode.Forbidden, unsigned.StatusCode);

        using var signed = new HttpRequestMessage(HttpMethod.Post, CommandPath) { Content = Content(raw) };
        signed.Headers.Add(GameWorkerAuthentication.AssertionHeader,
            GameWorkerAuthentication.CreateAssertion(GameId, "example-account", "POST", CommandPath,
                Encoding.UTF8.GetBytes(raw), key, DateTimeOffset.UtcNow));
        using var response = await client.SendAsync(signed);
        var reply = await ReadAsync(response);
        Assert.Equal(Hash(raw), reply.CommandSha256);
        Assert.Equal(reply.CommandSha256, reply.SignedSha256);
        Assert.Equal("signed", reply.Value);
        Assert.Equal(ExpectedTime, reply.At);
    }

    private static WebApplication Build(string mode)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Configuration["GameWorker:Mode"] = mode;
        builder.Configuration["AllowedHosts"] = "*";
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        app.UseRouting();
        return app;
    }

    private static void MapEndpoints(WebApplication app)
    {
        app.MapPost(CommandPath, (Command? command, HttpContext context) => Reply(context, command?.Value, command?.At));
        app.MapGet("/raw", (HttpContext context) => Reply(context));
        app.MapDelete("/raw", async (HttpContext context) =>
        {
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            return Reply(context, await reader.ReadToEndAsync(context.RequestAborted));
        });
    }

    private static bool IsFixtureLoopback(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote) &&
        context.Request.Host.Host == IPAddress.Loopback.ToString();

    private static HttpClient Client(WebApplication app) => new(new SocketsHttpHandler { UseProxy = false })
    { BaseAddress = new Uri(app.Urls.Single()) };

    private static HttpContent Content(string raw, string mediaType = "application/json") =>
        new ByteArrayContent(Encoding.UTF8.GetBytes(raw)) { Headers = { ContentType = new MediaTypeHeaderValue(mediaType) } };

    private static async Task<DigestReply> PostAsync(HttpClient client, string raw)
    {
        using var content = Content(raw);
        using var response = await client.PostAsync(CommandPath, content);
        return await ReadAsync(response);
    }

    private static async Task<DigestReply> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DigestReply>() ?? throw new InvalidOperationException("Digest reply is missing.");
    }

    private static DigestReply Reply(HttpContext context, string? value = null, DateTimeOffset? at = null) => new(
        GameWorkerAuthentication.GetCommandBodySha256(context), GameWorkerAuthentication.GetVerifiedBodySha256(context), value, at);

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    private sealed record Command(string? Value = null, DateTimeOffset? At = null);
    private sealed record DigestReply(string? CommandSha256, string? SignedSha256, string? Value, DateTimeOffset? At);
}
