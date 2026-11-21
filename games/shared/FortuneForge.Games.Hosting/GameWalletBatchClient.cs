using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FortuneForge.Games.Hosting;

internal sealed class GameWalletBatchClient(HttpClient http, string gameId, IConfiguration configuration,
    IHostEnvironment environment, TimeProvider clock) : IGameWalletBatchClient
{
    public async Task<GameWalletBatchReceipt> SettleBatchAsync(string actorAccountId, GameWalletBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (!GameWalletBatchVerifier.IsValidAccountId(actorAccountId) || !GameWalletBatchVerifier.IsValidRequest(request) ||
            !GameWalletBatchAuthentication.AllowsEffects(gameId, actorAccountId, request.Effects))
            throw new ArgumentException("The game wallet batch terms are invalid.", nameof(request));
        var frozen = request with { Effects = request.Effects.ToArray() };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var settings = configuration.GetSection("GameWallet");
            if (!Uri.TryCreate(settings["Url"], UriKind.Absolute, out var origin) || origin.AbsolutePath != "/" ||
                origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0 ||
                !(origin.Scheme == Uri.UriSchemeHttps || environment.IsDevelopment() && origin.IsLoopback && origin.Scheme == Uri.UriSchemeHttp) ||
                settings["BatchSigningKey"] is not { Length: > 0 } encodedKey)
                throw new GameWalletUnavailableException();
            var key = GameWorkerAuthentication.ReadSigningKey(encodedKey);
            var body = JsonSerializer.SerializeToUtf8Bytes(frozen, GameWalletAuthentication.WireJson);
            if (body.Length > GameWalletBatchAuthentication.MaximumBodyBytes)
                throw new GameWalletUnavailableException();
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, GameWalletBatchAuthentication.Route(gameId)))
            {
                Content = new ByteArrayContent(body),
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            message.Headers.Add(GameWalletAuthentication.AssertionHeader,
                GameWalletBatchAuthentication.CreateAssertion(gameId, actorAccountId, body, key, clock.GetUtcNow()));
            using var response = await http.SendAsync(message, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Conflict) throw new GameWalletConflictException();
            if (response.StatusCode != HttpStatusCode.OK ||
                !response.Headers.TryGetValues(GameWalletAuthentication.RequestSha256Header, out var hashes) ||
                !hashes.SequenceEqual([Convert.ToHexString(SHA256.HashData(body))], StringComparer.Ordinal))
                throw new GameWalletUnavailableException();

            var receipt = await response.Content.ReadFromJsonAsync<GameWalletBatchReceipt>(GameWalletAuthentication.WireJson, timeout.Token);
            return receipt is not null && GameWalletBatchVerifier.MatchesSettlement(receipt, gameId, actorAccountId, frozen)
                ? receipt : throw new GameWalletUnavailableException();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or FormatException or ArgumentException ||
            error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new GameWalletUnavailableException(error);
        }
    }
}

public static class GameWalletBatchClientRegistration
{
    public static IHttpClientBuilder AddGameWalletBatchClient(this IServiceCollection services, string gameId)
    {
        if (!GameWalletBatchAuthentication.IsSupportedGameId(gameId))
            throw new ArgumentException("This game is not configured for multi-account wallet settlement.", nameof(gameId));
        var client = services.AddHttpClient(nameof(GameWalletBatchClient))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
            .AddTypedClient((http, provider) => new GameWalletBatchClient(http, gameId,
                provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<IHostEnvironment>(),
                provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddTransient<IGameWalletBatchClient>(provider => provider.GetRequiredService<GameWalletBatchClient>());
        return client;
    }
}
