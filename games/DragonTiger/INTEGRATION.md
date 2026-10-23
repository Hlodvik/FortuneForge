# Host integration after the Baccarat release completes

All changes below are intentionally pending until the assignment's release gate opens. Preserve unrelated local changes and re-read each shared file immediately before patching it.

1. Root `package.json`: add workspace `games/DragonTiger/client/FortuneForge.Games.DragonTiger.Client`. App `fortuneforge.client/package.json`: add `@fortuneforge/games-dragon-tiger` version `0.1.0`. Regenerate only the necessary root lockfile entries; validate the existing game ownership/workspace checks.
2. Add `fortuneforge.client/src/app/routes/DragonTigerRoute.tsx` patterned on `BaccaratRoute.tsx`: `useAuthenticatedAccount('/games/dragon-tiger')`, the existing `InGameShell`, `PracticeModeNavAction`, `fetchWithAccountSession` and `practiceAccountFetch`. Import the new package and its `styles.css`; key the component and `playerId` by `${account.userId}:${practiceMode ? 'practice' : 'account'}`. Refresh `getCurrentAccount` on real wallet balance changes. The standalone component owns its game heading.
3. Register the lazy authenticated route in `AppRoutes.tsx`, add its title to `usePageTitle.ts`, and add a real entry on `pages/games/OtherGamesPage.tsx`. Route `/games/dragon-tiger`; title `Dragon Tiger`; category casino.
4. Add `games/catalog/games/dragon-tiger.game.json` using the existing schema, client package `@fortuneforge/games-dragon-tiger`, package version `0.1.0`, API base `/api/games/dragon-tiger`, and `rules-pilot` migration state until reviewed. The engine descriptor already exists in its own package.
5. Add a `ProjectReference` in `FortuneForge.Server/FortuneForge.Server.csproj` to `../games/DragonTiger/server/FortuneForge.Games.DragonTiger.Server/FortuneForge.Games.DragonTiger.Server.csproj`. Register core/server/sample/test projects in the relevant root and games `.slnx` files without changing existing entries.
6. In `FortuneForge.Server/Configuration/FortuneForgeStartup.cs`, import `FortuneForge.Games.DragonTiger.Server` and call:

```csharp
services.AddDragonTigerServer(options =>
{
    options.ReadRateLimitPolicy = RateLimitPolicies.SlotReads;
    options.WriteRateLimitPolicy = RateLimitPolicies.SlotSpins;
});
```

Map the package's endpoints in the server's existing endpoint mapping method, resolving the host's authenticated account:

```csharp
app.MapDragonTiger(async (context, cancellationToken) =>
{
    var profile = await context.RequestServices.GetRequiredService<AccountService>()
        .GetProfileAsync(AccountSessionCookie.Read(context.Request), cancellationToken);
    return profile.Value is { } account
        ? new DragonTigerAccount(account.UserId, account.Balances.SlotsCredits)
        : null;
});
```

Keep the normal account cookie, middleware and rate-limit policies. The package honors the existing `X-FortuneForge-Practice: true` header and keeps practice funds separate. It does not own authentication. `Features:DragonTigerEnabled` defaults false: explicitly enable only the intended reviewed environment. Development can enable the sample without affecting production.

7. Add the route to `scripts/game-smoke-inventory.mjs` and adjust inventory assertions in `scripts/verify-game-route-inventory.mjs` and `scripts/smoke-production.mjs` from 40 to 41, after verifying no other concurrent game changed that count. Extend `PackagedGameStyles.test.ts` and `CasinoTableShellRoutes.test.tsx` route coverage, add a route/account-practice integration test, and use the existing catalogue tests for the new entry.
8. Build both packages and the host, run ownership/route inventory verification, main app route tests and this package's tests. Exercise the connected authenticated route against an isolated emulator account (never production funds), checking both wallet and practice mode. Review only this game's diff. No commit, push or deploy is authorized by this task.
