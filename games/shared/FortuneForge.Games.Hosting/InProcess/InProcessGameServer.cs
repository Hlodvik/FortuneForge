using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;

namespace FortuneForge.Games.Hosting.InProcess;

/// <summary>Lets an ordinary ASP.NET host build and manage a game application without opening a socket.</summary>
public sealed class InProcessGameServer : IServer
{
    public InProcessGameServer(InProcessHttpEndpoint? endpoint = null)
    {
        Endpoint = endpoint ?? new InProcessHttpEndpoint();
        Features.Set<IServerAddressesFeature>(new AddressesFeature());
    }

    public InProcessHttpEndpoint Endpoint { get; }
    public IFeatureCollection Features { get; } = new FeatureCollection();

    public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();
        Endpoint.Bind(application);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Endpoint.StopAsync(cancellationToken);
    public void Dispose() => Endpoint.Dispose();

    private sealed class AddressesFeature : IServerAddressesFeature
    {
        public ICollection<string> Addresses { get; } = new List<string>();
        public bool PreferHostingUrls { get; set; }
    }
}
