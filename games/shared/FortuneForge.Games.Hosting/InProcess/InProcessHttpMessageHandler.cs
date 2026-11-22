namespace FortuneForge.Games.Hosting.InProcess;

/// <summary>Uses the endpoint's HTTP pipeline; authentication and authorization still execute normally.</summary>
public sealed class InProcessHttpMessageHandler(InProcessHttpEndpoint endpoint) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        endpoint.SendAsync(request, cancellationToken);
}
