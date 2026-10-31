using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;

namespace FortuneForge.Server.Activity;

internal interface IGameActivityAccountResolver
{
    Task<string?> UserIdAsync(HttpContext context, CancellationToken ct);
}

internal sealed class GameActivityAccountResolver(AccountService accounts) : IGameActivityAccountResolver
{
    public async Task<string?> UserIdAsync(HttpContext context, CancellationToken ct) =>
        (await accounts.GetAccessContextAsync(AccountSessionCookie.Read(context.Request), ct))?.UserId;
}
