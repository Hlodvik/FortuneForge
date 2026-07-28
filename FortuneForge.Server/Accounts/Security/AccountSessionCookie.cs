namespace FortuneForge.Server.Accounts.Security;

public static class AccountSessionCookie
{
    public const string Name = "__session";

    public static string? Read(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var bearerToken = authorization["Bearer ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                return bearerToken;
            }
        }

        return request.Cookies.TryGetValue(Name, out var cookieToken)
            ? cookieToken
            : null;
    }

    public static void Write(
        HttpResponse response,
        string token,
        DateTime expiresAtUtc,
        bool secure,
        bool remainLoggedIn)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true
        };

        // A persistent cookie backs "Remain logged in". Otherwise the browser
        // removes the cookie when its session ends even though the server-side
        // session remains valid for its normal security lifetime.
        if (remainLoggedIn)
        {
            options.Expires = new DateTimeOffset(expiresAtUtc);
            options.MaxAge = expiresAtUtc - DateTime.UtcNow;
        }

        response.Cookies.Append(Name, token, options);
    }

    public static void Delete(HttpResponse response, bool secure)
    {
        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true
        });
    }
}
