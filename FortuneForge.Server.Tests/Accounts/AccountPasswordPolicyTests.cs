using System.Reflection;
using FortuneForge.Server.Accounts;
using FortuneForge.Server.Accounts.Models;
using FortuneForge.Server.Accounts.Security;
using FortuneForge.Server.Accounts.Storage;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FortuneForge.Server.Tests.Accounts;

public sealed class AccountPasswordPolicyTests
{
    [Theory]
    [InlineData("Password1!", true)]
    [InlineData("password1!", false)]
    [InlineData("Password!", false)]
    [InlineData("Password1", false)]
    [InlineData("Password1 ", false)]
    [InlineData("Password1é", false)]
    [InlineData("Password1💎", true)]
    [InlineData("Password1\u0001", false)]
    [InlineData("Ab1!", false)]
    public void New_password_requires_length_uppercase_digit_and_symbol(string password, bool expected)
    {
        Assert.Equal(expected, AccountPasswordPolicy.IsValidNewPassword(password));
    }

    [Fact]
    public void New_password_has_a_128_character_maximum()
    {
        Assert.True(AccountPasswordPolicy.IsValidNewPassword("Ab1!" + new string('x', 124)));
        Assert.False(AccountPasswordPolicy.IsValidNewPassword("Ab1!" + new string('x', 125)));
    }

    [Theory]
    [InlineData("password1!")]
    [InlineData("Password!")]
    [InlineData("Password1")]
    public async Task Account_creation_rejects_new_passwords_missing_a_requirement(string password)
    {
        // Validation must happen before account storage or Firebase registration.
        var service = new AccountService(null!, null!, null!, null!);
        var result = await service.CreateAsync(
            new CreateAccountRequest("Alice", "alice@example.test", password), default);

        Assert.Null(result.Value);
        Assert.Equal(AccountError.InvalidPassword, result.Error);
    }

    [Theory]
    [InlineData("password1!")]
    [InlineData("Password!")]
    [InlineData("Password1")]
    public async Task Password_change_rejects_new_passwords_missing_a_requirement(string password)
    {
        var store = DispatchProxy.Create<IAccountStore, PasswordStoreProxy>();
        var calls = (PasswordStoreProxy)store;
        var service = new AccountService(store, new LegacyPasswordHasher(), null!,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var result = await service.ChangePasswordAsync(new string('a', 40),
            new ChangePasswordRequest("legacy-password", password), default);

        Assert.Null(result.Value);
        Assert.Equal(AccountError.InvalidPassword, result.Error);
        Assert.Equal(0, calls.PasswordUpdates);
    }

    [Fact]
    public async Task Password_change_accepts_a_strong_new_password_for_an_existing_legacy_password()
    {
        var store = DispatchProxy.Create<IAccountStore, PasswordStoreProxy>();
        var calls = (PasswordStoreProxy)store;
        var service = new AccountService(store, new LegacyPasswordHasher(), null!,
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var result = await service.ChangePasswordAsync(new string('a', 40),
            new ChangePasswordRequest("legacy-password", "NewPassword1!"), default);

        Assert.NotNull(result.Value);
        Assert.Equal(AccountError.None, result.Error);
        Assert.Equal(1, calls.PasswordUpdates);
        Assert.Equal("hashed:NewPassword1!", calls.NewHash);
    }

    public class PasswordStoreProxy : DispatchProxy
    {
        public int PasswordUpdates;
        public string? NewHash;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == nameof(IAccountStore.ResolveSessionAsync))
                return Task.FromResult<string?>("alice");

            if (method?.Name == nameof(IAccountStore.FindByIdAsync))
            {
                var account = new AccountSummary("alice", "Alice", "alice@example.test",
                    DateTime.UtcNow, new AccountBalances(0, 0),
                    new SlotStatistics(0, 0, 0, 0, 0, 0), "player");
                return Task.FromResult<StoredAccount?>(
                    new StoredAccount(account, "ALICE", "legacy-hash", "active", false));
            }

            if (method?.Name == nameof(IAccountStore.UpdatePasswordHashAsync))
            {
                PasswordUpdates++;
                NewHash = (string?)args?[1];
                return Task.FromResult(true);
            }

            throw new NotSupportedException(method?.Name);
        }
    }

    private sealed class LegacyPasswordHasher : IPasswordHashingService
    {
        public string Hash(string password) => "hashed:" + password;
        public bool Verify(string password, string encodedHash) =>
            password == "legacy-password" && encodedHash == "legacy-hash";
    }
}
