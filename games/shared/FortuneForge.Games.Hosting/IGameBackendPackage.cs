using Microsoft.AspNetCore.Builder;

namespace FortuneForge.Games.Hosting;

public interface IGameBackendPackage
{
    string GameId { get; }

    WebApplication Create(WebApplicationOptions options, Action<WebApplicationBuilder> configure);
}
