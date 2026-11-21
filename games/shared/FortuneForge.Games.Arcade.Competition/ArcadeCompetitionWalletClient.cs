using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.Wallet;
using FortuneForge.Games.Hosting;
using FortuneForge.Server.Arcade.Competition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FortuneForge.Games.Arcade.Competition;

internal sealed class ArcadeCompetitionWalletClient(HttpClient http, string gameId, IConfiguration configuration,
    IHostEnvironment environment, TimeProvider clock) : IArcadeCompetitionEntryWallet, IArcadeCompetitionPayoutCreditor
{
    public Task<GameWalletSettlementReceipt> ChargeAsync(ArcadeCompetitionEntryCharge charge, CancellationToken token)
    {
        if (charge?.Entry is not { } entry || !ArcadeCompetitionWalletProtocol.IsEntryCharge(gameId, entry.PlayerId, charge))
            throw new ArgumentException("The arcade competition wallet entry terms are invalid.", nameof(charge));
        return PostAsync<ArcadeCompetitionEntryCharge, GameWalletSettlementReceipt>(entry.PlayerId,
            ArcadeCompetitionWalletProtocol.EntryRoute(gameId), charge,
            receipt => ArcadeCompetitionWalletProtocol.MatchesEntryReceipt(entry, receipt), token);
    }

    public async Task<bool> VerifyLegacyChargeAsync(ArcadeCompetitionEntryDebit entry, CancellationToken token)
    {
        if (entry is null || !ArcadeCompetitionWalletProtocol.IsEntryDebit(gameId, entry.PlayerId, entry))
            throw new ArgumentException("The arcade competition wallet entry proof terms are invalid.", nameof(entry));
        var proof = await PostAsync<ArcadeCompetitionEntryDebit, ArcadeCompetitionEntryProof>(entry.PlayerId,
            ArcadeCompetitionWalletProtocol.EntryProofRoute(gameId), entry, static _ => true, token);
        return proof.Verified;
    }

    public Task<ArcadeCompetitionPayoutCreditResult> CreditAsync(ArcadeCompetitionIdentity competition,
        ArcadeCompetitionCreditInstruction instruction, DateTimeOffset creditedAtUtc, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var charge = new ArcadeCompetitionPayoutCharge(competition, instruction, creditedAtUtc);
        if (!ArcadeCompetitionWalletProtocol.IsPayoutCharge(gameId, instruction.PlayerId, charge))
            throw new ArgumentException("The arcade competition wallet payout terms are invalid.", nameof(instruction));
        return PostAsync<ArcadeCompetitionPayoutCharge, ArcadeCompetitionPayoutCreditResult>(instruction.PlayerId,
            ArcadeCompetitionWalletProtocol.PayoutRoute(gameId), charge,
            receipt => ArcadeCompetitionWalletProtocol.MatchesPayoutReceipt(instruction, receipt), token);
    }

    private async Task<TReply> PostAsync<TRequest, TReply>(string actor, string path, TRequest request,
        Func<TReply, bool> verify, CancellationToken token) where TReply : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var (origin, key) = ReadConnection();
            var body = JsonSerializer.SerializeToUtf8Bytes(request, GameWalletAuthentication.WireJson);
            if (body.Length > GameWalletAuthentication.MaximumBodyBytes) throw new GameWalletUnavailableException();
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, path))
            {
                Content = new ByteArrayContent(body),
            };
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            message.Headers.Add(GameWalletAuthentication.AssertionHeader,
                GameWalletAuthentication.CreatePostAssertion(gameId, actor, path, body, key, clock.GetUtcNow()));

            using var response = await http.SendAsync(message, timeout.Token);
            if (response.StatusCode == HttpStatusCode.Conflict) throw new GameWalletConflictException();
            if (response.StatusCode != HttpStatusCode.OK ||
                !response.Headers.TryGetValues(GameWalletAuthentication.RequestSha256Header, out var hashes) ||
                !hashes.SequenceEqual([Convert.ToHexString(SHA256.HashData(body))], StringComparer.Ordinal))
                throw new GameWalletUnavailableException();

            var reply = await response.Content.ReadFromJsonAsync<TReply>(GameWalletAuthentication.WireJson, timeout.Token);
            return reply is not null && verify(reply) ? reply : throw new GameWalletUnavailableException();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or FormatException or ArgumentException ||
            error is OperationCanceledException && !token.IsCancellationRequested)
        {
            throw new GameWalletUnavailableException(error);
        }
    }

    private (Uri Origin, byte[] Key) ReadConnection()
    {
        var encodedKey = configuration["GameWallet:SigningKey"];
        if (!Uri.TryCreate(configuration["GameWallet:Url"], UriKind.Absolute, out var origin) ||
            origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0 ||
            !(origin.Scheme == Uri.UriSchemeHttps || environment.IsDevelopment() && origin.IsLoopback && origin.Scheme == Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(encodedKey))
            throw new GameWalletUnavailableException();
        return (origin, GameWorkerAuthentication.ReadSigningKey(encodedKey));
    }
}

public static class ArcadeCompetitionWalletRegistration
{
    public static IHttpClientBuilder AddArcadeCompetitionWallet(this IServiceCollection services, string gameId)
    {
        if (!GameWalletAuthentication.IsValidGameId(gameId))
            throw new ArgumentException("The arcade competition wallet game ID is invalid.", nameof(gameId));
        var client = services.AddHttpClient(nameof(ArcadeCompetitionWalletClient))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
            .AddTypedClient((http, provider) => new ArcadeCompetitionWalletClient(http, gameId,
                provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<IHostEnvironment>(),
                provider.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddTransient<IArcadeCompetitionEntryWallet>(provider => provider.GetRequiredService<ArcadeCompetitionWalletClient>());
        services.AddTransient<IArcadeCompetitionPayoutCreditor>(provider => provider.GetRequiredService<ArcadeCompetitionWalletClient>());
        return client;
    }
}
