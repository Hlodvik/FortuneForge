using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Security;

namespace FortuneForge.Server.Admin.Operations;

internal enum AdminOperationsAccessStatus
{
    Authorized,
    Unauthenticated,
    Forbidden
}

internal sealed record AdminOperationsAccess(
    AdminOperationsAccessStatus Status,
    string? UserId = null);

internal interface IAdminOperationsAuthorizer
{
    Task<AdminOperationsAccess> AuthorizeAsync(
        HttpRequest request,
        CancellationToken cancellationToken);
}

internal sealed class AccountAdminOperationsAuthorizer(AccountService accounts)
    : IAdminOperationsAuthorizer
{
    public async Task<AdminOperationsAccess> AuthorizeAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var access = await accounts.GetAccessContextAsync(
            AccountSessionCookie.Read(request),
            cancellationToken);
        if (access is null) return new(AdminOperationsAccessStatus.Unauthenticated);
        return access.IsAdmin
            ? new(AdminOperationsAccessStatus.Authorized, access.UserId)
            : new(AdminOperationsAccessStatus.Forbidden);
    }
}
