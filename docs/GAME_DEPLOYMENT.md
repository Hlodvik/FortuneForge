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
dotnet ./FortuneForge.Games.NeonNights.Server.dll --urls http://127.0.0.1:5193
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
Credentials for the worker's OIDC audience token and retains account admission
and wallet settlement. [Cloud Run secret references](https://cloud.google.com/run/docs/configuring/services/secrets)

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
Its worker resolves paid cascade outcomes and compatible legacy free spins.
The site continues to own authentication, entitlement checks, and settlement.

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
