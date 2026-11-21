# Andar Bahar server integration

The package is integrated into the central host in local source as of October 24, 2026; these audit fixes have not been deployed. The package implements the documented **single deck, Andar first, Joker revealed when the deal begins** variant. Andar wins return stake plus 0.9 times stake; Bahar wins return stake plus equal stake. There is no tie, surrender, or separate cash-out: every accepted wager settles completely before its response. The November migration uses a durable package intent, signed wallet settlement, then an atomic game finalization. The paced card presentation is a replay of that immutable settlement.

## Current paid worker integration — 21 November 2026

The packaged worker owns paid admission, immutable deal intent, signed settlement requests, outcomes, history, audit and interrupted-round recovery. It registers `AddAndarBaharServer` and `AddGameWalletClient("andar-bahar")`. Worker `GameWallet:Url` and `GameWallet:SigningKey` must match the generic account service's per-game signing key. Enable `GameWallet:Games:andar-bahar:AllowAccountBalanceRead`; use the standard wallet ledger with no `LegacySlotLedger`, reservations or free-game allowance.

The shared host authenticates and forwards paid routes to the configured worker. It must retain exact-key and GET/latest forwarding while new starts are paused and fail unavailable when the worker cannot be reached. Authorized host practice remains separate. The browser binds paid requests to the account that opened the table using `X-FortuneForge-Expected-Account` and preserves pending commands on session expiry or account mismatch. The historical registration recipe below remains relevant to the local practice surface.

## Historical central host wiring

1. `FortuneForge.Server/FortuneForge.Server.csproj` references:

```xml
<ProjectReference Include="..\games\AndarBahar\server\FortuneForge.Games.AndarBahar.Server\FortuneForge.Games.AndarBahar.Server.csproj" />
```

2. `Configuration/FortuneForgeStartup.cs` registers:

```csharp
builder.Services.AddAndarBaharServer(options =>
{
    options.ReadRateLimitPolicy = RateLimitPolicies.SlotReads;
    options.WriteRateLimitPolicy = RateLimitPolicies.SlotSpins;
    options.AllowPracticeRequests = true;
    options.PracticeStatePath = Path.Combine(Path.GetTempPath(), "fortuneforge-andar-bahar", "practice.json");
});
```

`ForcePractice` defaults to false. The package's `AllowPracticeRequests` default remains false; the central host explicitly enables authenticated, account-scoped practice using a separate file ledger, consistent with its QA policy. This ledger is ephemeral across host replacement, contains no cash, and never touches the paid Firestore wallet. Other hosts that leave practice disabled return 403 for explicit practice requests rather than silently spending paid funds. The paid adapter uses the host's existing `FirestoreDb` registration.

3. `Configuration/FortuneForgePipeline.cs` maps after routing/rate-limiter/authorization middleware and before the fallback:

```csharp
app.MapAndarBahar(async (context, cancellationToken) =>
{
    var accounts = context.RequestServices.GetRequiredService<AccountService>();
    var profile = (await accounts.GetProfileAsync(
        AccountSessionCookie.Read(context.Request), cancellationToken)).Value;
    return profile is null ? null : new AndarBaharAccount(
        profile.UserId, profile.Balances.SlotsCredits);
});
```

The resolver owns authentication. No request parameter can select another player. `WalletBalance` preserves the existing account-resolver convention; `/status` and round responses read the actual stored wallet balance, rather than trusting that potentially stale value.

4. The central development and main appsettings enable `Features:AndarBaharEnabled` in local source; live activation requires the separate approved release. When disabled, authenticated status returns `available:false`; owned round/latest reads and an exact replay of an already committed key remain available. New keys are rejected with 503 inside the atomic store operation, and changed-body replays still return 409. Disabling new play cannot hide an already paid wager or create a new one through the recovery path. The standalone sample hardcodes this feature on and `ForcePractice=true`; it cannot reach a production wallet.

5. The game engine, server, sample and tests are included in the game and central solutions. Focused Andar Bahar and shared-history checks should run when these server contracts change; unrelated gameplay suites are not required for isolated visual edits.

## Paid storage and privacy

`AndarBaharWalletStore` freezes a schema-3 `andarBaharRounds` intent with its original request observation and only the Joker/revealed card prefix. `andarBaharAccounts` holds the pending pointer. The generic signed wallet owns the debit, credit and durable wallet receipt. The game package never writes `userBalances` or `balanceTransactions`.

After confirmation, one game transaction stores the normalized wallet receipt, clears the pending pointer, advances the latest round, writes `cardGameResults/andar-bahar-{roundId}`, and invokes the shared audit/participation writers. History contains exact wager, payout and net cents with side, winner and timestamps; it contains no cards, deck, balance or idempotency key. Replays preserve acknowledged history and do not move the latest pointer backwards. A declined key remains declined after a top-up.

`AndarBaharFirestoreStore` is read-only compatibility for schemas 1 and 2. It validates saved wagers (including the former R100 cap), revealed cards, ledger identities, settlement events and schema-2 linked history. Schema 1 does not fabricate retrospective history. Neither historical recovery nor a new signed round can charge twice. Corrupt persisted proof fails closed. These private server-managed collections retain the existing access policy; no hidden deck tail is stored or returned.

## HTTP contract

Base: `/api/games/andar-bahar`. All replies set `Cache-Control: no-store, max-age=0` and vary by account cookie/practice header. Money is decimal rand; all internal accounting uses integer cents.

`GET /status`:

```json
{"available":true,"minimumStake":0.5,"maximumStake":50,"stakeIncrement":0.5,"balance":10000,"mode":"development-practice-andar-first"}
```

Paid mode is `andar-first`. `POST /rounds` requires `Content-Type: application/json` and an `Idempotency-Key` of 16–128 ASCII letters/digits/hyphens/underscores. The body accepts exactly `side` (`andar` or `bahar`) and `stake` (R0.50–R50 for new wagers, R0.50 steps); unknown properties, client cards, and client outcomes are rejected. An exact-key retry of an older settled wager above R50 remains recoverable.

```json
{"side":"andar","stake":0.5}
```

201 response, also used by `GET /rounds/{roundId}` and `GET /rounds/latest`:

```json
{
  "roundId":"<64-character sha256>","balance":10000.45,"phase":"settled",
  "joker":{"rank":"ace","suit":"spades"},
  "deals":[{"number":1,"side":"andar","card":{"rank":"ace","suit":"hearts"}}],
  "winner":"andar","side":"andar","stake":0.5,"disposition":"win","profit":0.45,"totalReturn":0.95
}
```

Ranks and suits are lowercase shared-card enum names (`ace`, `two` … `king`; `clubs`, `diamonds`, `hearts`, `spades`). `deals.number` is 1-based, with at most 49 deals. `profit` is net change: −stake on a loss. `totalReturn` includes the winning stake and is zero on a loss. The response has no user identity, raw idempotency key, undealt deck, or RNG state.

`GET /rounds/latest` returns 204 if none exists. Reading another player's round returns 404. Repeating a new schema-3 key and wager returns the same round and its original wallet receipt balance; historical schemas 1 and 2 preserve their current-wallet-balance convention. Reusing the key with a different side/stake returns 409. Replaying an old key does not move the latest pointer backwards.

Errors use `{ "code": "...", "message": "..." }`: 400 `andar-bahar-invalid-request`, 401 `andar-bahar-authentication-required`, 403 `andar-bahar-practice-disabled`, 404 `andar-bahar-round-not-found`, 409 `andar-bahar-round-conflict` or `insufficient-slot-credits`, 503 `andar-bahar-disabled` or `andar-bahar-retry-required`, and 500 `andar-bahar-unavailable`. A transient 503 includes `Retry-After: 1`. After a lost/ambiguous response, retry the original key and wager; never create a replacement key to recover it.

## Local preview

```powershell
dotnet run --project games/AndarBahar/samples/FortuneForge.Games.AndarBahar.Web -c Release
```

The loopback-only API uses port 5202 and CORS origins `http://127.0.0.1:5182` / `http://localhost:5182`. The sample labels itself **DEVELOPMENT PRACTICE ONLY**, starts each new local account with R10,000 practice credit, and uses an HTTP-only random local cookie. Browser requests must use `credentials: 'include'`, including initial status, so subsequent requests and refreshes share the same practice account. Use the same hostname for API and UI.

State is atomically flushed and renamed at `samples/FortuneForge.Games.AndarBahar.Web/.practice/andar-bahar.json`; this directory is ignored. Exclusive file locking serializes separate processes, and a crash releases the lock. A corrupt file is preserved and rejected. In sandboxed environments, launching the loopback sample with the normal local execution permission may be required for the browser to reach its port.

## Verification performed on 2026-10-20

`dotnet build games/AndarBahar/samples/FortuneForge.Games.AndarBahar.Web -c Release` passed with zero warnings/errors. `dotnet test games/AndarBahar/tests/FortuneForge.Games.AndarBahar.Tests -c Release` passed **79 tests**, none skipped, including 13 real local Firestore emulator cases. Coverage includes exact Andar/Bahar payouts, integer-cent preservation, multi-instance same-key replay, competing-key overdraft prevention, lost-response recovery, latest/get privacy, strict request validation, insufficient funds, feature/authentication gates, corrupt settlement/ledger/event rejection, and durable practice restart behavior.

A real HTTP smoke test on port 5202 accepted a 50-cent Andar practice wager and returned 25 paced deal cards, 45-cent profit, and R10,000.45 balance. The host process was stopped and restarted; `GET /rounds/latest` plus the original `POST /rounds` key recovered the identical round and unchanged balance. No production funds were used. The API remains running for the owner preview.
