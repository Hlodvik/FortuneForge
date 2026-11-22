namespace FortuneForge.Server.GameWorkers.Packages;

public interface IGamePackageTransport
{
    bool Contains(string gameId);
    Task<HttpResponseMessage> SendAsync(string gameId, HttpRequestMessage request, CancellationToken token);
}
