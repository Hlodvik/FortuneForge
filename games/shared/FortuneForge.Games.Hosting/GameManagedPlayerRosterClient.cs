using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using FortuneForge.Games.Abstractions.ManagedPlayers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FortuneForge.Games.Hosting;

public static class GameManagedPlayerRosterRegistration
{
    private const string ClientName = "game-managed-player-roster";

    public static IServiceCollection AddManagedPlayerRosterClient(this IServiceCollection services, string gameId)
    {
        if (!GameManagedPlayerRosterProtocol.IsValidGameId(gameId)) throw new ArgumentException("Invalid game ID.", nameof(gameId));
        services.AddHttpClient(ClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.MaxResponseContentBufferSize = GameManagedPlayerRosterProtocol.MaximumBodyBytes;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        services.AddSingleton<IManagedPlayerRosterClient>(provider => new GameManagedPlayerRosterClient(
            provider.GetRequiredService<IHttpClientFactory>(), provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<IHostEnvironment>(), provider.GetService<TimeProvider>() ?? TimeProvider.System, gameId));
        return services;
    }

    private sealed class GameManagedPlayerRosterClient(IHttpClientFactory factory, IConfiguration configuration,
        IHostEnvironment environment, TimeProvider clock, string gameId) : IManagedPlayerRosterClient
    {
        public async Task<IReadOnlyList<ManagedPlayerRosterProfile>> ReserveAsync(string assignmentId, int count,
            IReadOnlyCollection<string> excludedProfileIds, DateTime nowUtc, CancellationToken cancellationToken)
        {
            var excluded = excludedProfileIds.ToArray();
            if (count is < 1 or > GameManagedPlayerRosterProtocol.MaximumCount ||
                !GameManagedPlayerRosterProtocol.AreValidProfileIds(excluded, GameManagedPlayerRosterProtocol.MaximumExclusions))
                throw new ArgumentException("Invalid roster size or exclusions.", nameof(count));
            var request = new ManagedPlayerReserveRequest(assignmentId, count, excluded, nowUtc);
            var response = await SendAsync(GameManagedPlayerRosterProtocol.ReserveAction, assignmentId, nowUtc, request,
                HttpStatusCode.OK, cancellationToken);
            ManagedPlayerRosterProfile[] result;
            try
            {
                result = JsonSerializer.Deserialize<ManagedPlayerRosterProfile[]>(response, GameManagedPlayerRosterProtocol.WireJson)
                    ?? throw new ManagedPlayerRosterUnavailableException();
            }
            catch (JsonException exception) { throw new ManagedPlayerRosterUnavailableException(exception); }
            if (result.Length != 0 && result.Length != count ||
                result.Any(profile => profile is null || string.IsNullOrWhiteSpace(profile.PlayerName) ||
                    profile.SkillLevel is not (ManagedPlayerSkillLevels.Poor or ManagedPlayerSkillLevels.Average or ManagedPlayerSkillLevels.Strong)) ||
                !GameManagedPlayerRosterProtocol.AreValidProfileIds(result.Select(profile => profile.UserId).ToArray(), count) ||
                result.Select(profile => profile.UserId).Distinct(StringComparer.Ordinal).Count() != result.Length ||
                result.Any(profile => excluded.Contains(profile.UserId, StringComparer.Ordinal)))
                throw new ManagedPlayerRosterUnavailableException();
            return result;
        }

        public Task HeartbeatAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc,
            CancellationToken cancellationToken) => LeaseAsync(GameManagedPlayerRosterProtocol.HeartbeatAction,
                assignmentId, profileIds, nowUtc, cancellationToken);

        public async Task<bool> TryRenewAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            var request = LeaseRequest(assignmentId, profileIds, nowUtc);
            var response = await SendAsync(GameManagedPlayerRosterProtocol.TryRenewAction, assignmentId, nowUtc, request,
                HttpStatusCode.OK, cancellationToken);
            try { return JsonSerializer.Deserialize<bool>(response, GameManagedPlayerRosterProtocol.WireJson); }
            catch (JsonException exception) { throw new ManagedPlayerRosterUnavailableException(exception); }
        }

        public Task ReleaseAsync(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc,
            CancellationToken cancellationToken) => LeaseAsync(GameManagedPlayerRosterProtocol.ReleaseAction,
                assignmentId, profileIds, nowUtc, cancellationToken);

        private Task LeaseAsync(string action, string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc,
            CancellationToken cancellationToken) => SendAsync(action, assignmentId, nowUtc, LeaseRequest(assignmentId, profileIds, nowUtc),
                HttpStatusCode.NoContent, cancellationToken);

        private static ManagedPlayerLeaseRequest LeaseRequest(string assignmentId, IReadOnlyCollection<string> profileIds, DateTime nowUtc)
        {
            var ids = profileIds.ToArray();
            if (!GameManagedPlayerRosterProtocol.AreValidProfileIds(ids, GameManagedPlayerRosterProtocol.MaximumCount))
                throw new ArgumentException("Invalid managed profile IDs.", nameof(profileIds));
            return new(assignmentId, ids, nowUtc);
        }

        private async Task<byte[]> SendAsync<T>(string action, string assignmentId, DateTime nowUtc, T request,
            HttpStatusCode expected, CancellationToken cancellationToken)
        {
            if (!GameManagedPlayerRosterProtocol.IsValidAssignment(assignmentId) || nowUtc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("A valid assignment ID and UTC timestamp are required.", nameof(assignmentId));
            try
            {
                var (origin, key) = ReadConnection();
                var body = JsonSerializer.SerializeToUtf8Bytes(request, GameManagedPlayerRosterProtocol.WireJson);
                if (body.Length > GameManagedPlayerRosterProtocol.MaximumBodyBytes)
                    throw new ManagedPlayerRosterUnavailableException();
                using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(origin, GameManagedPlayerRosterProtocol.Route(gameId, action)))
                {
                    Content = new ByteArrayContent(body)
                };
                message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                message.Headers.Add(GameManagedPlayerRosterProtocol.AssertionHeader,
                    GameManagedPlayerRosterProtocol.CreateAssertion(gameId, action, assignmentId, body, key, clock.GetUtcNow()));
                using var http = factory.CreateClient(ClientName);
                using var reply = await http.SendAsync(message, cancellationToken);
                if (reply.StatusCode != expected ||
                    !reply.Headers.TryGetValues(GameManagedPlayerRosterProtocol.RequestSha256Header, out var hashes) ||
                    !hashes.SequenceEqual([Convert.ToHexString(SHA256.HashData(body))], StringComparer.Ordinal))
                    throw new ManagedPlayerRosterUnavailableException();
                return await reply.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or FormatException or ArgumentException ||
                exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                throw new ManagedPlayerRosterUnavailableException(exception);
            }
        }

        private (Uri Origin, byte[] Key) ReadConnection()
        {
            var encodedKey = configuration["ManagedPlayerRoster:SigningKey"];
            if (!Uri.TryCreate(configuration["ManagedPlayerRoster:Url"], UriKind.Absolute, out var origin) ||
                origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0 ||
                !(origin.Scheme == Uri.UriSchemeHttps || environment.IsDevelopment() && origin.IsLoopback && origin.Scheme == Uri.UriSchemeHttp) ||
                string.IsNullOrWhiteSpace(encodedKey))
                throw new ManagedPlayerRosterUnavailableException();
            return (origin, GameWorkerAuthentication.ReadSigningKey(encodedKey));
        }
    }
}
