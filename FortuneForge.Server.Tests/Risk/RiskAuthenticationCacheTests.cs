using System.Reflection;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Storage;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FortuneForge.Server.Tests.Risk;

public sealed class RiskAuthenticationCacheTests
{
    public class AccountStoreProxy : DispatchProxy
    {
        public int Resolves;
        public int Reads;
        public bool Revoked;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IAccountStore.ResolveSessionAsync))
            { Resolves++; return Task.FromResult<string?>(Revoked ? null : "alice"); }
            if (method.Name == nameof(IAccountStore.FindByIdAsync))
            {
                Reads++;
                var summary = new AccountSummary("alice", "Alice", "alice@example.test", DateTime.UtcNow, new(5, 0), new(0, 0, 0, 0, 0, 0), "player");
                return Task.FromResult<StoredAccount?>(new(summary, "ALICE", "hash", "active", false));
            }
            throw new NotSupportedException(method.Name);
        }
    }

    [Fact]
    public async Task Middleware_and_endpoint_share_only_same_request_same_token_authentication()
    {
        var store = DispatchProxy.Create<IAccountStore, AccountStoreProxy>(); var calls = (AccountStoreProxy)store;
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var service = new AccountService(store, null!, null!, accessor); var token = new string('a', 40);
        Assert.NotNull(await service.GetAccessContextAsync(token, default));
        Assert.NotNull((await service.GetProfileAsync(token, default)).Value);
        Assert.Equal(1, calls.Resolves); Assert.Equal(1, calls.Reads);
        Assert.NotNull(await service.GetAccessContextAsync(new string('b', 40), default));
        Assert.Equal(2, calls.Resolves);
        // A fresh HTTP request must see a revocation immediately.
        accessor.HttpContext = new DefaultHttpContext(); calls.Revoked = true;
        Assert.Null(await service.GetAccessContextAsync(token, default));
        Assert.Equal(3, calls.Resolves);
        Assert.Null((await service.GetProfileAsync(token, default)).Value);
        Assert.Equal(4, calls.Resolves); // failed authentication is never cached
        Assert.Null(await service.GetAccessContextAsync("too-short", default));
        Assert.Equal(4, calls.Resolves);
    }
}
