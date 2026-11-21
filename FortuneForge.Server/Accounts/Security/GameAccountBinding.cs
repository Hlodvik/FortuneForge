namespace FortuneForge.Server.Accounts.Security;

internal static class GameAccountBinding
{
    internal const string Header = "X-FortuneForge-Expected-Account";

    internal static bool Matches(HttpRequest request, string accountId) =>
        !request.Headers.TryGetValue(Header, out var expected) || expected.Count == 1 && expected[0] == accountId;

    internal static object SessionChanged { get; } = new
    {
        code = "game-session-changed",
        error = "Sign back in to the account that opened this game."
    };
}
