using FortuneForge.Games.Hosting;

namespace FortuneForge.Games.GameCompute.Server;

internal static class GameComputeEndpoints
{
    internal static void MapComputeAction<TRequest, TOutcome>(
        this WebApplication app, string route, Func<TRequest, TOutcome> action,
        Func<Exception, bool>? isRuleViolation = null)
    {
        if (!route.StartsWith("/internal/v1/", StringComparison.Ordinal))
            throw new ArgumentException("Game compute actions require an internal route.", nameof(route));

        app.MapGameAction(route, (TRequest request) =>
        {
            try
            {
                return action(request);
            }
            catch (Exception exception) when (isRuleViolation?.Invoke(exception) == true)
            {
                throw new ArgumentException(exception.Message, exception);
            }
        });
    }
}
