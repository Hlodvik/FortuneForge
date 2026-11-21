# Independent game releases

The shared release command builds one game's browser and backend artifacts.
Each game registers its entry points in its own `game-release.json`. The game ID
comes from that configuration, so it can differ from the npm workspace and
internal API identifiers.

From the repository root, build Neon Nights and serve its browser release.

```powershell
npm run deploy:game -- neon-nights
npx vite preview --outDir artifacts/game-releases --host 127.0.0.1 --port 4193 --strictPort
```

In another PowerShell terminal, start the website with the local release host.

```powershell
$env:VITE_GAME_RELEASE_BASE_URL = 'http://127.0.0.1:4193'
npm run dev --workspace fortuneforge.client
```

Open `/slots/neon-nights/demo`. The website loads the active browser module and
CSS with its shared React runtime. The standard site API still handles play in
the website demo.

The command writes the runnable backend to the private
`artifacts/game-engine-releases/games/<game-id>/releases/<backend-release-id>`
directory. The active browser manifest also records that backend release ID.
To run the Neon engine locally, start its DLL from that directory.

```powershell
dotnet ./FortuneForge.Games.PackagedGame.Server.dll --urls http://127.0.0.1:5193
```

The engine responds at `/healthz`. Wallet-backed slot workers calculate
outcomes from signed host admissions at `POST /internal/v1/paid/spins`.

For an art, audio, or UI release, use `npm run deploy:game -- neon-nights --assets-only`.
The new browser release keeps the backend release selected by the previous
combined release.

Add `--stage` to build the artifacts and print their release ID without making
them active. Use `--promote <release-id>` to select a staged release or
`--rollback <release-id>` to restore a previously active one. Both update the
active pointer for the specified game.

The default command publishes local artifacts. Combined local releases are
registered for the 19 slots that use the shared slots API, Jade Dragon, and
the games listed below. Live browser and worker releases use separate commands.

### Private Cloud Run workers

Stage one current game build with `npm run deploy:game -- jade-dragon --stage`.
Use the resulting backend release ID. The worker publisher builds a runtime
image from that staged executable and its dependencies. It submits only the
isolated runtime context to Cloud Build, then deploys the resulting image by
digest to `fortuneforge-game-<game-id>`. [Cloud Build CLI](https://cloud.google.com/sdk/gcloud/reference/builds/submit)

Before enrolling a game, configure Cloud Run, Cloud Build, and a Docker
repository in Artifact Registry in the selected project and region. Supply a
worker runtime service account, the host's existing caller service account,
and a per-game Secret Manager secret containing a base64 signing key of at least
32 bytes. Grant the worker identity access to that secret version and configure
the Cloud Build identity to write to the repository. The deployer needs Cloud
Run deployment permissions and permission to use the runtime identity.

Prepare and review a local image context before making a cloud change.

```powershell
$workerArgs = @(
  'jade-dragon', '--publish-worker', '<staged-release-id>',
  '--worker-project', '<project-id>', '--worker-region', '<region>',
  '--worker-repository', '<docker-repository-name>',
  '--worker-service-account', '<worker-runtime-service-account-email>',
  '--worker-caller-service-account', '<host-service-account-email>',
  '--worker-secret', '<secret-name>:<numeric-version>'
)
npm run deploy:game -- @workerArgs --enroll-worker --worker-plan
```

`--worker-plan` uses only local files. It writes the Docker context under
`artifacts/game-worker-contexts` and prints the destination, identities, and
secret reference.

For first-time enrollment only, run
`npm run deploy:game -- @workerArgs --enroll-worker`. Enrollment requires the
service name to be unused, creates an IAM-authenticated service, and grants the
specified host identity `roles/run.invoker` on that service. The service uses
the normal HTTPS `run.app` origin for authenticated traffic. Cloud Run checks
the caller's OIDC identity, and the game worker checks the signed request.
[Cloud Run service authentication](https://cloud.google.com/run/docs/authenticating/service-to-service)

Packaged Firestore workers enroll in hosted mode with the selected project as
their Firestore project and their game features enabled. The host continues to
control which routes accept play. Grant their runtime identity the game’s
Firestore access before enrollment. Hi-Lo and Mines allow the project's
`web.app` and `firebaseapp.com` site origins. Later publishes check these
settings on the enrolled worker before staging a revision.

After enrollment, prepare a reviewed JSON file outside the source checkout.
Use the runtime game ID returned by the worker's `/healthz` locally or
`/Healthz` on Cloud Run. Include the worker's HTTPS origin and the same-project
Secret Manager name and numeric version. Jade Dragon's release name is
`jade-dragon` and its runtime game ID is `empty-slots-v1`.

```json
{
  "bindings": [
    {
      "gameId": "empty-slots-v1",
      "url": "https://fortuneforge-game-jade-dragon-example-ue.a.run.app",
      "signingSecret": "jade-dragon-worker-signing:1"
    }
  ]
}
```

Give the host runtime identity access to the pinned secret. Use the guarded
API release to apply the reviewed bindings. `-PreflightOnly` displays the
validated plan locally.

```powershell
./scripts/deploy.ps1 -Target api -GameWorkerBindingsFile $bindingsPath -PreflightOnly
./scripts/deploy.ps1 -Target api -GameWorkerBindingsFile $bindingsPath
```

The API release adds or updates `GameWorkers__<game-id>__Url` and a secret
reference at `GameWorkers__<game-id>__SigningKey`. It verifies the new revision's
bindings before switching traffic. The host uses Google Application Default
Credentials for the worker's OIDC audience token. The host authenticates accounts,
routes signed requests and settles generic wallet operations. [Cloud Run secret references](https://cloud.google.com/run/docs/configuring/services/secrets)

Before deploying the gateway for 2048 and Drop Merge, configure each worker
URL and signing key. New sessions require the corresponding worker.
`GameWorkers__<game-id>__ForwardPublicRoutes=true` also forwards the status
route. Both workers retain their existing compute endpoints. The gateway keeps
version 4 session routes on the host and forwards version 7 session routes during
a pause on new starts. Keep the worker binding during a pause. Deploy the gateway
with paged journal verification and retention before enabling new starts. Legacy
in-memory sessions are tied to the host instance that created them.

The Arcane Archives worker keeps the paid outcome endpoint and serves the
existing Tome and potion routes. Set `GameWorkers__arcane-archives-v1__ForwardPublicRoutes=true`
before switching traffic to the gateway that requires the worker for Tome and
potion writes. Local verification uses a loopback Firestore emulator and a
`demo-*` project.

The Arcane worker can own new paid rounds after the gateway and wallet endpoint
are ready. Bind `GameWallet__Url` to the HTTPS wallet origin and bind
`GameWallet__SigningKey` to a separate per-game secret. Bind the matching secret
on the wallet at `GameWallet__Games__arcane-archives-v1__SigningKey`. Enable
`GameWallet__Games__arcane-archives-v1__LegacySlotLedger` and
`GameWallet__Games__arcane-archives-v1__AllowLegacyFreeGames` to retain past
slot ledger IDs and allow players to redeem carried spins. The wallet binds the
game and account to the signed settlement instead of reading identity from the
request body.

Drain unkeyed host admissions and verify the worker bindings for every slot
before deploying this gateway. New slot rounds require their workers. Set
`Slots__ArcaneWorkerRoundsEnabled=true` on the gateway and
`Arcane__AllowNewPaidRounds=true` on the worker. The gateway uses its legacy
recovery path only when the worker identifies an existing host-owned operation.
`Arcane__AllowNewPaidRounds=false` pauses new rounds while allowing exact retries
of saved worker rounds. Keep forwarding enabled during that pause. Older gateways
must honor the durable admission marker before they can serve as rollback targets
with prepared rounds still open.

Neon Nights forwards new base spins, Disco plays, Car plays, Car respins, Nitro
choices, and administrator feature grants when `Slots:NeonWorkerRoundsEnabled`
is enabled on the gateway. The worker owns new Car entitlements. The wallet
binding for `neon-nights-v1` uses a separate signing secret and enables
`LegacySlotLedger`, `AllowLegacyFreeGames`, `AllowAccountBalanceRead`,
`AllowCashWagerWithLegacyFreeGame`, and `AllowWagerReservations`. A carried play
can charge the locked wager and consume its old entitlement in one wallet
transaction. Newly issued Car plays keep their entitlement in Neon state.
A spin-origin Nitro choice becomes visible once the wallet has reserved its wager.

Drain legacy admissions with no durable choice before enabling forwarding.
Known spin-origin Nitro choices and historical receipts can still use the
exact legacy recovery route. The worker can recover saved Car respin choices.
Keep forwarding enabled once native rounds exist. To pause native starts,
turn off `Neon:AllowNewPaidRounds` in the worker and keep wallet reservations
enabled so held Nitro choices can settle.

Jade Dragon forwards new spins and saved play state when the gateway enables
`Slots:JadeDragonWorkerRoundsEnabled` for `empty-slots-v1`. Configure a separate
wallet signing secret and enable `LegacySlotLedger` and `AllowLegacyFreeGames`
for this worker. Historical free games keep their saved math model, feature
mode, and wager. New awards belong to the game package. The worker imports old
Jade game counters once when it takes ownership of the saved state.

Drain unsettled legacy admissions before enabling Jade forwarding. To pause new
starts, disable `JadeDragon:AllowNewPaidRounds` in the worker and keep forwarding
enabled so existing operation IDs can recover.

Gold Foundry forwards paid spins and Crucible state when the gateway enables
`Slots:GoldFoundryWorkerRoundsEnabled`. Its private worker imports the
historical Crucible charge, qualifying wagers, and pity once. Bind a separate
wallet key for `gold-foundry-v1` with `LegacySlotLedger` and
`AllowAccountBalanceRead`. The game submits the table win and Crucible
proceeds as one settlement and displays their amounts separately.

Drain unsettled legacy admissions before enabling Gold Foundry forwarding.
Disable `GoldFoundry:AllowNewPaidRounds` in the worker to pause new spins and
keep its gateway forwarding enabled for exact recovery.

Midnight Vault uses release ID `midnight-vault` and runtime ID `dragon-hoard-v1`.
When `Slots:MidnightVaultWorkerRoundsEnabled` is enabled, its worker owns
paid rounds, free-game queue order, boosts, and seal progress. Its separate
wallet binding uses `LegacySlotLedger`, `AllowLegacyFreeGames`, and
`AllowAccountBalanceRead`. The worker honors the imported queue before new
awards and retains each entry's original wager and math version.

Resolve any existing host admission before switching the gateway to Midnight
Vault. The prior host's stale-admission recovery uses the old before-and-after
snapshot to restore an already consumed free game or boost. Keep forwarding
enabled when pausing new starts with `MidnightVault:AllowNewPaidRounds`.

Wukong uses release ID `wukong` and signed runtime ID `wukong-journey-v1`.
Enable `Slots:WukongWorkerRoundsEnabled` to route paid rounds and state to the
worker. Its wallet binding uses `LegacySlotLedger`, `AllowLegacyFreeGames`,
and `AllowAccountBalanceRead`. The wallet resolves the historical player
balance at `classic-demo-v1` within the signed Wukong scope. The worker
preserves the saved Celestial Quest wager and mode. For old banks without a
saved wager, the first free spin supplies a valid cash wager to lock.

Resolve any unsettled host admission before enabling Wukong forwarding. The
old path may already have consumed a free game or boost. Pause new rounds
with `Wukong:AllowNewPaidRounds` while keeping forwarding enabled for recovery.

Rainbow Realm uses release ID `rainbow-realm` and runtime ID
`rainbow-realm-fruits-v1`. Enable `Slots:RainbowRealmWorkerRoundsEnabled` to
route paid rounds and state to its worker. Its wallet binding uses
`LegacySlotLedger`, `AllowLegacyFreeGames`, and `AllowAccountBalanceRead`.
The worker carries forward the Orchard Rush balance, locked wager and mode,
and charm progress. Resolve unsettled host admissions before switching, and
pause only new rounds with `RainbowRealm:AllowNewPaidRounds` during recovery.

Pirates' Fortune uses release ID `pirates-fortune` and runtime ID
`pirates-fortune-v1`. Enable `Slots:PiratesFortuneWorkerRoundsEnabled` to route
paid rounds and state to the signed worker. Its wallet binding uses
`LegacySlotLedger`, `AllowLegacyFreeGames`, and `AllowAccountBalanceRead`.
The worker preserves saved bonus plays, earned wagers, composite bonus modes,
and gem totals. Resolve an unsettled host admission before switching. Use
`PiratesFortune:AllowNewPaidRounds` to pause new rounds during recovery.

Gods of Olympus uses release ID `gods-of-olympus` and runtime ID
`gods-of-olympus-v1`. Enable `Slots:GodsOfOlympusWorkerRoundsEnabled` to route
paid rounds and state to its worker. Its wallet binding uses `LegacySlotLedger`,
`AllowLegacyFreeGames`, and `AllowAccountBalanceRead`. Existing classic free
spins keep their earned wager and mode. New cash games use the current tumble
rules. Resolve any unsettled host admission before switching. Pause new rounds
with `GodsOfOlympus:AllowNewPaidRounds` while leaving recovery available.

Reel Riches uses release ID `reel-riches` and runtime ID `reel-riches-v1`.
Enable `Slots:ReelRichesWorkerRoundsEnabled` to route paid rounds and state to
the game worker. Its wallet binding uses `LegacySlotLedger`,
`AllowLegacyFreeGames`, and `AllowAccountBalanceRead`. Existing bonus spins
retain their saved wager and mode. Reel Riches preserves collected seals and
the energy meter. Resolve any unsettled host admission before switching. Use
`ReelRiches:AllowNewPaidRounds` to pause new rounds and leave recovery available.

High Noon Fortune uses release ID `high-noon-fortune` and runtime ID
`high-noon-fortune-v1`. Enable `Slots:HighNoonFortuneWorkerRoundsEnabled` to
route paid rounds and state to the game worker. Its wallet binding uses
`LegacySlotLedger`, `AllowLegacyFreeGames`, and `AllowAccountBalanceRead`.
Existing bonus plays keep their earned wager and mode. Resolve any unsettled
host admission before switching, and use `HighNoonFortune:AllowNewPaidRounds`
to pause new starts while keeping recovery available.

Royal Draw uses release ID `royal-draw` and runtime ID `royal-draw-v1`.
Enable `Slots:RoyalDrawWorkerRoundsEnabled` to route paid rounds and state
to the game worker. Its wallet binding uses `LegacySlotLedger`,
`AllowLegacyFreeGames`, and `AllowAccountBalanceRead`. Bonus spins retain
their saved wager and mode, and the game imports its seal and energy progress.
Resolve unsettled host admissions before switching. Pause new starts with
`RoyalDraw:AllowNewPaidRounds` while keeping recovery available.

Cosmic Fortune and Ocean Odyssey use release IDs `cosmic-fortune` and
`ocean-odyssey` and runtime IDs `cosmic-fortune-v1` and `ocean-odyssey-v1`.
Their gateway switches are `Slots:CosmicFortuneWorkerRoundsEnabled` and
`Slots:OceanOdysseyWorkerRoundsEnabled`. The workers use `LegacySlotLedger`,
`AllowLegacyFreeGames`, and `AllowAccountBalanceRead`. Pause new paid starts
with `CosmicFortune:AllowNewPaidRounds` or `OceanOdyssey:AllowNewPaidRounds`.

Before enabling a classic-slot worker, allow host admissions to finish. The
gateway can close an expired classic admission when the saved guard, account,
round state, and debit receipts identify the exact spent feature credits. The
transaction restores those credits once and asks the player to refresh before
spinning again. Resolve any ambiguous prior wager lock or missing evidence
through the existing reconciliation process before enabling the title for those
accounts. Arcane's keyed compatibility path can replay its saved outcome.

Publish the affected workers and gateway before publishing clients that submit keyed paid spins
to `/api/slots/prepared-spins`. This endpoint accepts only configured
prepared workers and requires the original operation ID. Older gateways
return an error at this route, preserving the saved operation for recovery.

Stage the Asteroids private worker with `npm run deploy:game -- asteroids --backend-only --stage`.
The command produces a backend release ID for `--publish-worker`. The existing
catalogue competition page remains the browser entry.

For each later game update, stage the new game build and use its release ID in
`$workerArgs`. Run `npm run deploy:game -- @workerArgs` to build the image and
create a ready revision with zero traffic. The publisher checks the existing
worker's service label, runtime identity, signing secret reference, and private
IAM policy before deployment. Subsequent publishes reuse the enrolled service
and its caller permissions. Add `--activate-worker` when publishing a new revision to
send that game's traffic to it immediately after verification. To activate an
already staged revision or roll back, explicitly select its revision on the
same Cloud Run service with `gcloud run services update-traffic`.
[Cloud Run revisions and traffic](https://cloud.google.com/run/docs/rollouts-rollbacks-traffic-migration)

Worker publishing changes only the selected game's service. Changes to the
host's first-time worker configuration and changes to browser presentation
follow their own release paths.

### Cloud browser assets

The assets-only publisher uses a Cloud Storage bucket set up to serve game
artifacts with CORS for the site. The website build and release terminal use
`VITE_GAME_RELEASE_BASE_URL=https://storage.googleapis.com/<bucket>/`. The
release terminal uses the active `gcloud` account for writes. Once that host is
configured, stage a local release, upload its browser files, then inspect and
promote its pointer using the returned generation.

```powershell
npm run deploy:game -- neon-nights --assets-only --stage
npm run deploy:game -- neon-nights --assets-only --browser-bucket <bucket> --publish-browser <release-id>
npm run deploy:game -- neon-nights --assets-only --browser-bucket <bucket> --inspect
npm run deploy:game -- neon-nights --assets-only --browser-bucket <bucket> --promote <release-id> --if-generation <generation>
```

Each uploaded release uses an immutable path. Promotion updates only that game's
browser pointer and keeps any backend release already selected there. An absent
pointer reports generation `0`. Roll back by using `--rollback` with a retained
browser release ID and the current generation.

### Local package coverage

Classic slots publish their own manifest, styles, and media using the shared slot
view. Their backend build selects one engine project and uses the shared
`FortuneForge.Games.ClassicSlot.Server` executable.

Jade Dragon publishes its own HTML entry and artwork for the site's iframe.
Its packaged worker owns the paid round, free-game state, and saved-round
recovery. The site authenticates the player, forwards requests, and provides
the signed shared wallet service.

Andar Bahar, Craps, Dragon Tiger, Hi-Lo, and Mines publish browser modules and
selected `FortuneForge.Games.PackagedGame.Server` workers. For local integration
work, these workers require a loopback Firestore emulator and an explicit test
account.

2048, Baccarat, Blackjack, Casino War, Drop Merge, Hearts, House Hold’em, Keno,
Roulette, Sic Bo, Competitive Solitaire, Texas Hold’em, Three Card Poker, and Video Poker
publish browser modules and selected
`FortuneForge.Games.GameCompute.Server` workers. Their internal HTTP endpoints
execute game rules. The site API still owns account sessions, wallet
transactions, and multiplayer coordination.

Free Solitaire uses the `free-solitaire` release and Competitive Solitaire uses
the `solitaire` release. Each release builds its own browser entry and worker
from the shared Solitaire board and rules. The site selects the corresponding
package for each route.
