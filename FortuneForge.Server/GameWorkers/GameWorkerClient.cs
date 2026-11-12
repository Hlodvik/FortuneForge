using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Auditing;
using FortuneForge.Games.Hosting;
using Google.Apis.Auth.OAuth2;

namespace FortuneForge.Server.GameWorkers;

public sealed class GameWorkerUnavailableException(string gameId, Exception? inner = null)
    : Exception($"The {gameId} game service is temporarily unavailable.", inner);

public sealed class GameWorkerRequestRejectedException()
    : ArgumentException("The game service rejected the requested action.");

/// <summary>Calls a private, independently released game worker on behalf of an authenticated player.</summary>
public sealed class GameWorkerClient(HttpClient http, IConfiguration configuration, IHostEnvironment environment,
    TimeProvider clock, ILogger<GameWorkerClient> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, OidcToken> IdentityTokens = new(StringComparer.Ordinal);

    public bool IsConfigured(string gameId) => !string.IsNullOrWhiteSpace(configuration[$"GameWorkers:{gameId}:Url"]);

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(string gameId, string userId,
        string route, TRequest request, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(request, Json);
        using var response = await SendAsync(gameId, userId, HttpMethod.Post, route, body,
            "application/json", null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            throw new GameWorkerRequestRejectedException();
        if (!response.IsSuccessStatusCode)
            throw new GameWorkerUnavailableException(gameId);
        try
        {
            return await response.Content.ReadFromJsonAsync<TResponse>(Json, cancellationToken)
                ?? throw new JsonException("The worker returned no result.");
        }
        catch (JsonException error)
        {
            throw new GameWorkerUnavailableException(gameId, error);
        }
    }

    internal async Task<HttpResponseMessage> SendAsync(string gameId, string userId, HttpMethod method,
        string route, byte[] body, string? contentType, IReadOnlyDictionary<string, string>? headers,
        CancellationToken cancellationToken)
    {
        var settings = configuration.GetSection($"GameWorkers:{gameId}");
        if (!Uri.TryCreate(settings["Url"], UriKind.Absolute, out var origin) ||
            origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0 ||
            !(origin.Scheme == Uri.UriSchemeHttps || environment.IsDevelopment() && origin.IsLoopback && origin.Scheme == Uri.UriSchemeHttp) ||
            !route.StartsWith('/') || route.StartsWith("//", StringComparison.Ordinal) || route.Contains('\\') ||
            string.IsNullOrWhiteSpace(settings["SigningKey"]))
            throw new GameWorkerUnavailableException(gameId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var token = timeout.Token;
        try
        {
            var key = GameWorkerAuthentication.ReadSigningKey(settings["SigningKey"]!);
            var destination = new Uri(origin, route);
            using var message = new HttpRequestMessage(method, destination);
            if (body.Length > 0 || contentType is not null)
            {
                message.Content = new ByteArrayContent(body);
                if (contentType is not null) message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            }
            if (!environment.IsDevelopment() || !origin.IsLoopback)
            {
                var identity = await GetIdentityTokenAsync(origin.GetLeftPart(UriPartial.Authority), token);
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", identity);
            }
            foreach (var name in GameWorkerAuthentication.AssertedHeaderNames)
                if (headers?.TryGetValue(name, out var value) == true)
                    message.Headers.TryAddWithoutValidation(name, value);
            message.Headers.Add(GameWorkerAuthentication.AssertionHeader,
                GameWorkerAuthentication.CreateAssertion(gameId, userId, method.Method, destination.PathAndQuery,
                    body, key, clock.GetUtcNow(), GameSecurityContext.Current, headers));

            return await http.SendAsync(message, token);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or
            FormatException or ArgumentException or InvalidOperationException or OperationCanceledException)
        {
            logger.LogWarning("Game worker {GameId} request failed ({ErrorType}).", gameId, error.GetType().Name);
            throw new GameWorkerUnavailableException(gameId, error);
        }
    }

    private async Task<string> GetIdentityTokenAsync(string audience, CancellationToken cancellationToken)
    {
        if (!IdentityTokens.TryGetValue(audience, out var token))
        {
            var credential = await GoogleCredential.GetApplicationDefaultAsync(cancellationToken);
            var created = await credential.GetOidcTokenAsync(OidcTokenOptions.FromTargetAudience(audience), cancellationToken);
            token = IdentityTokens.GetOrAdd(audience, created);
        }
        return await token.GetAccessTokenAsync(cancellationToken);
    }
}
