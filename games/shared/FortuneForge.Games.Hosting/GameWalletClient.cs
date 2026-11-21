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

internal sealed class GameWalletClient(HttpClient http, string gameId, IConfiguration configuration,
    IHostEnvironment environment, TimeProvider clock) : IGameWalletClient, IGameWalletAccountReader
{
    public async Task<GameWalletSettlementReceipt> SettleAsync(string accountId, GameWalletSettlementRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeout(cancellationToken);
        try
        {
            var (origin, key) = ReadConnection();
            var body = JsonSerializer.SerializeToUtf8Bytes(request, GameWalletAuthentication.WireJson);
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, GameWalletAuthentication.SettlementRoute(gameId)))
            {
                Content = new ByteArrayContent(body),
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            message.Headers.Add(GameWalletAuthentication.AssertionHeader,
                GameWalletAuthentication.CreateSettlementAssertion(gameId, accountId, body, key, clock.GetUtcNow()));

            using var response = await http.SendAsync(message, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Conflict)
                throw new GameWalletConflictException();
            if (response.StatusCode != HttpStatusCode.OK)
                throw new GameWalletUnavailableException();

            if (!MatchesHeader(response, GameWalletAuthentication.RequestSha256Header, Convert.ToHexString(SHA256.HashData(body))))
                throw new GameWalletUnavailableException();

            var receipt = await response.Content.ReadFromJsonAsync<GameWalletSettlementReceipt>(GameWalletAuthentication.WireJson, timeout.Token);
            if (receipt is null || !GameWalletReceiptVerifier.MatchesSettlement(receipt, gameId, accountId, request))
                throw new GameWalletUnavailableException();
            return receipt;
        }
        catch (Exception error) when (IsTransportError(error, cancellationToken))
        {
            throw new GameWalletUnavailableException(error);
        }
    }

    public async Task<GameWalletAccountState> GetStateAsync(string accountId, CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeout(cancellationToken);
        try
        {
            var (origin, key) = ReadConnection();
            using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(origin, GameWalletAuthentication.AccountStateRoute(gameId)));
            message.Headers.Add(GameWalletAuthentication.AssertionHeader,
                GameWalletAuthentication.CreateAccountStateAssertion(gameId, accountId, key, clock.GetUtcNow()));

            using var response = await http.SendAsync(message, timeout.Token);
            if (response.StatusCode != HttpStatusCode.OK || !MatchesHeader(response, GameWalletAuthentication.StateScopeHeader,
                GameWalletAuthentication.AccountStateScope(gameId, accountId)))
                throw new GameWalletUnavailableException();

            var state = await response.Content.ReadFromJsonAsync<GameWalletAccountState>(GameWalletAuthentication.WireJson, timeout.Token);
            return state is { LegacyFreeGamesRemaining: >= 0, SlotsCreditsBalanceCents: null or >= 0 } &&
                (state.PlayerName is null || GameWalletAccountState.IsValidPlayerName(state.PlayerName))
                ? state : throw new GameWalletUnavailableException();
        }
        catch (Exception error) when (IsTransportError(error, cancellationToken))
        {
            throw new GameWalletUnavailableException(error);
        }
    }

    private (Uri Origin, byte[] Key) ReadConnection()
    {
        var encodedKey = configuration["GameWallet:SigningKey"];
        if (!GameWalletAuthentication.IsValidGameId(gameId) ||
            !Uri.TryCreate(configuration["GameWallet:Url"], UriKind.Absolute, out var origin) ||
            origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0 ||
            !(origin.Scheme == Uri.UriSchemeHttps || environment.IsDevelopment() && origin.IsLoopback && origin.Scheme == Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(encodedKey))
            throw new GameWalletUnavailableException();
        return (origin, GameWorkerAuthentication.ReadSigningKey(encodedKey));
    }

    private static CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        return timeout;
    }

    private static bool MatchesHeader(HttpResponseMessage response, string header, string expected) =>
        response.Headers.TryGetValues(header, out var values) && values.SequenceEqual([expected], StringComparer.Ordinal);

    private static bool IsTransportError(Exception error, CancellationToken cancellationToken) =>
        error is HttpRequestException or IOException or JsonException or FormatException or ArgumentException ||
        error is OperationCanceledException && !cancellationToken.IsCancellationRequested;

}

public static class GameWalletClientRegistration
{
    public static IHttpClientBuilder AddGameWalletClient(this IServiceCollection services, string gameId)
    {
        var client = services.AddHttpClient(nameof(GameWalletClient))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
            .AddTypedClient((http, provider) => new GameWalletClient(http, gameId,
                provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<IHostEnvironment>(),
                provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddTransient<IGameWalletClient>(provider => provider.GetRequiredService<GameWalletClient>());
        services.AddTransient<IGameWalletAccountReader>(provider => provider.GetRequiredService<GameWalletClient>());
        return client;
    }
}
