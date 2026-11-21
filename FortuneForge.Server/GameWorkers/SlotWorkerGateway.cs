using System.Net;
using System.Text.Json;
using FortuneForge.Games.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace FortuneForge.Server.GameWorkers;

internal static class SlotWorkerGateway
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static Task<ContentResult> ReadStateAsync(GameWorkerClient? workers, HttpContext context,
        string gameId, string accountId, string path, CancellationToken cancellationToken) =>
        ForwardAsync(workers, context, gameId, accountId, HttpMethod.Get, path, [], null, null, cancellationToken);

    internal static Task<ContentResult> PostAsync<TCommand>(GameWorkerClient? workers, HttpContext context,
        string gameId, string accountId, string path, TCommand command, string? operationId, CancellationToken cancellationToken)
    {
        var actualPath = context.Request.Path.Value;
        if (string.Equals(actualPath?.TrimEnd('/'), path, StringComparison.OrdinalIgnoreCase))
            path = actualPath!;
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in GameWorkerAuthentication.AssertedHeaderNames)
            if (context.Request.Headers.TryGetValue(name, out var value))
                headers.Add(name, value.ToString());
        if (operationId is not null)
            headers["Idempotency-Key"] = operationId;
        return ForwardAsync(workers, context, gameId, accountId, HttpMethod.Post, path,
            JsonSerializer.SerializeToUtf8Bytes(command, Json), "application/json", headers, cancellationToken);
    }

    internal static bool IsConflict(ContentResult response, string code)
    {
        if (response.StatusCode != (int)HttpStatusCode.Conflict || string.IsNullOrEmpty(response.Content))
            return false;
        try
        {
            using var document = JsonDocument.Parse(response.Content);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("code", out var value) &&
                value.ValueKind == JsonValueKind.String && value.GetString() == code;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task<ContentResult> ForwardAsync(GameWorkerClient? workers, HttpContext context,
        string gameId, string accountId, HttpMethod method, string path, byte[] body, string? contentType,
        IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        if (workers?.IsConfigured(gameId) != true)
            throw new GameWorkerUnavailableException(gameId);
        using var upstream = await workers.SendAsync(gameId, accountId, method, path, body, contentType,
            headers, cancellationToken, preserveResponse: true);
        var upstreamType = upstream.Content.Headers.ContentType;
        if (upstreamType?.MediaType is { } mediaType && !string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) &&
            !mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
            throw new GameWorkerUnavailableException(gameId);
        if (upstream.Headers.RetryAfter is { } retryAfter)
            context.Response.Headers.RetryAfter = retryAfter.ToString();
        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            ContentType = upstreamType?.ToString() ?? "application/json",
            Content = await upstream.Content.ReadAsStringAsync(cancellationToken)
        };
    }
}
