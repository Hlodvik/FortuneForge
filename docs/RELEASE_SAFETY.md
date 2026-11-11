# Release safety

`main` is the only release history. Deployment uses the **existing checkout** at
its exact committed `main` HEAD. It never creates a worktree, repository copy or
branch, switches commits, stashes/resets/cleans source, or deletes a release folder.
A push does not deploy.

## Normal release

After reconciling and committing the intended change to `main`, reserve this
checkout exclusively for the release and first run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/deploy.ps1 -PreflightOnly
```

This local-only check runs before any credential, live-service, install or build
command. Dirty tracked/staged/untracked files, a different candidate, detached
HEAD, another branch, hidden index flags, and uncommitted tooling refuse the
release without changing source. `-Commit SHA` must resolve to that same active
`main` HEAD; an older application commit cannot be paired with newer tooling.
Prepare a reviewed revert commit for a rollback rather than switching this
checkout during deployment. The script never reconciles source automatically.

Once the exact committed candidate passes preflight, run `npm run deploy`, or
use `npm run deploy:hosting` / `npm run deploy:api`. The
Firebase commands use the active gcloud account, which must also already be
signed in to Firebase CLI, so the two tools cannot silently choose different
default users. The
active deploy script, guard module, guard tests, and release verifier must match
their committed versions on the candidate, including the navbar/CI guards and
upload exclusions. **All source must be clean**; unrelated development edits
cannot remain in this checkout during a release. The script takes a cooperative
release lock, then holds Windows read-share handles for every tracked regular
source file. These handles deny writes/deletes throughout verification and
publication. Symlinks/submodules, masked index entries and locked bytes that
do not match their Git objects fail closed. No unlocked publication fallback is
provided on other operating systems; the existing `.cmd` tooling path and file
locking publication path are Windows-specific.

The script runs `npm ci` and the exact candidate's committed verifier, plus its
deployment guard tests. Candidate HEAD/branch/cleanliness is checked around each
verification command and before publication. The release record includes source
mode, commit, Git tree, an ordinal path/size/SHA-256 byte-manifest digest and file
count. Source and workspace handles are disposed on success and failure. Prior
release directories are preserved. Tests use synthetic Git responses and tiny
plain-text lock fixtures, never new repositories or worktrees.

Local auto-loaded `.env*` files (except `.env.example`), local server settings,
and external `VITE_` variables are refused as implicit build inputs at every
command boundary and immediately before stamping/publication, including files
added after initial preflight. Use reviewed committed code
configuration. `.gcloudignore` includes the committed root `.gitignore` and
explicit nested sample-store exclusions. The local SDK selector preserves
those exclusions, and every selected upload file must also be a tracked,
locked candidate file. Unknown/ignored extras fail rather than being uploaded.
The script generates a strict ignore allowlist for that exact intersection:
all other files, including new siblings created after inventory, are excluded.
Unsupported glob-like filenames fail closed. The generated allowlist has its
own read-share handle and recorded byte hash until the release finishes.
Reproducibility still relies on trusted tooling/dependencies and
the checkout being reserved against other build processes; the guard is not a
general operating-system sandbox.

The 250,000-spin slot Monte Carlo job is **not** part of every
deployment. Run `npm run verify:slot-math` separately when slot math changes.
The simulator requires observed RTP below 95%. The default run includes the
active Olympus tumble model as well as the legacy bonus-carry model;
`--olympus-demo` selects the tumble model alone, and `--seed=<seed>` selects
an independent run. For the November 3, 2026 slot release, Arcane
Archives' hit-rate target and Rainbow Realm's RTP target remain visible as
advisories. The 250,000-cycle runs measured 88.279% RTP and a 35.973% hit rate
for Arcane, and 90.080% RTP against an 88% ± 2-point target for Rainbow.
Neither game's payouts or reel strips changed for release acceptance. The
other configured targets and bankroll depletion checks still apply.
The new Olympus tumble model reduced one symbol's base payout from 3× to
2.5× without changing its draw weights or match tiers. At 250,000 spins per
wager, it measured 93.962% RTP with independent holdouts of 93.831% and
92.929%. Historical v1 and v2 outcomes still replay under their original rules.

Hosting-only releases also inspect the serving API source stamp. The API must
already be at the candidate, or be a locally available ancestor whose complete
delta contains only explicitly allowed frontend/test/documentation/tooling
paths. Backend, catalogue, deployment-runtime or unknown paths require a combined
API-and-Hosting release. Missing stamps/objects and divergent history fail
closed. A paid Craps frontend cannot be published alone while its required API
changes are absent. Immediately before Hosting publication the API identity is
rechecked; a combined release must be serving the exact stamped candidate API.
No test-suite waiver bypasses this compatibility gate.

### Automatic visual-only verification

For Hosting-only releases, the guard compares the actual live Hosting source
stamp with the complete selected candidate tree, not just its latest commit.
Exactly one registered game's CSS, image/font assets (including SVG), and
colocated tests/docs may qualify. At least one visual change is required.
Application TypeScript/TSX, gateways, wallet/settlement code, manifests, lockfiles,
configuration, shared files, backend code, multiple games, deletions/renames,
unusual file modes, or any uncertainty use the full suite. API/combined releases,
rollbacks, adoption, and missing/unavailable baselines default to the full suite.
There is no force-scoped override.

Qualified releases run the affected game's checks and any test script, every game build,
shared web tests, lint/build, ownership/route checks, release guardrails and the
normal full production smoke. Only unrelated game checks/tests and .NET tests
are skipped. Clean checkouts still need every game built for package exports.
The verifier independently recalculates the plan. The release record saves the
baseline, candidate, tooling SHA, mode, workspace, reason, and changed paths.
Standalone `npm run verify:release` defaults to the full suite.

### Explicit test-free deployment

When the owner explicitly requests deployment without any pre-deployment test
runner, use the dedicated route:

```powershell
npm run deploy:test-free
```

It deploys both API and Hosting by default. Pass a narrower target after npm's
argument separator when needed:

```powershell
npm run deploy:test-free -- -Target hosting
npm run deploy:test-free -- -Target api
```

This route skips the game and host npm test suites, all .NET test projects,
focused live-playtest scopes, and the deployment guardrail test script. It
retains dependency installation, source/route/navbar/CI-order policy checks,
game type checks and builds, lint, the web production build, immutable-source
and live-target race guards, release stamps, and publication integrity checks.
The normal production smoke still runs after publication to confirm the
deployed endpoints. The release record identifies the test-free mode and its
fixed explicit waiver reason; it never reports skipped tests as passed.

This route controls only the local guarded deployment. A push or pull request
can still trigger the independent GitHub Actions test workflow; use the existing
documented `[skip ci]` commit-message mechanism separately when appropriate.
`-TestFree` cannot be combined with `-LivePlaytest`, whose purpose is to retain
the fixed focused backend test scopes. The ordinary deployment routes keep their
existing verification behavior.

### Explicit Hosting test-suite waiver

When the owner explicitly requests deployment without application test suites,
use `scripts/deploy.ps1 -Target hosting -SkipTestSuites -TestWaiverReason 'Owner requested skipping test suites for the reviewed visual change.'`.
Both flags are required together. API/combined releases reject this waiver
unless the separate explicit live-playtest mode below is supplied, before reading live services. This is not a force-scoped override: the live-to-candidate
plan is still independently calculated and may remain `full`. Only application
npm test suites and .NET test runs are omitted. Type checks, every game build,
lint, web build, ownership/route checks, focused deployment guard tests, immutable
source/main/ancestry/live-race checks, release stamps and production smoke remain.
The release record stores `testSuiteWaiver.skipped` and its reason; output says
application tests were **NOT RUN**, never that they passed. Standalone verification
does not accept this waiver.

### Explicit matched live playtest

For an owner-authorized incremental API/Hosting playtest while broad assurance
is deferred, use the exact committed candidate on clean current main:

```powershell
$candidate = (git rev-parse HEAD).Trim()
.\scripts\deploy.ps1 -Target all -Commit $candidate -PreflightOnly
.\scripts\deploy.ps1 -Target all -Commit $candidate -LivePlaytest -SkipTestSuites -TestWaiverReason 'Owner authorized incremental matched live playtest; broad suites and CI deferred until all slot adjustments are accepted.'
```

`-LivePlaytest` requires API/all scope, `-SkipTestSuites`, a nonblank reason,
and immutable guarded verification. Both entry points independently calculate
the `live-playtest` plan. It retains source/route/navbar/CI-order policy checks,
all game type checks/builds, lint, the web production build, and a fresh Release
build of the server test project. Six fixed server test scopes then run: Slots,
paid Craps store, paid Craps replay/financial audit, Activity, card history, and
investigation. There is no caller-supplied filter or evidence-reuse override.
Every scope must produce a fresh nonempty TRX report with every test passed;
zero matches, missing reports, skips, failures and aborted runs refuse publication.
Reports remain under the unique ignored `artifacts/releases/focused-*` directory.
Broad npm application tests and the full server .NET test project are deferred;
this mode does not establish full-suite or slot calibration acceptance.

The release record stores the mode, waiver reason, deferred broad suites, fixed
focused scopes and their status. Default releases retain full verification.
Every clean-source/upload/private-file guard, missing-only database publication
and readiness check, immutable image digest, live-target race guard, matched API
before Hosting requirement, release stamp and production smoke remains mandatory.

The deploy scripts do not query GitHub CI status and never claim it passed.
The current workflow runs on main pushes, pull requests and manual dispatch.
GitHub supports `[skip ci]` in the pushed commit message for push/PR workflows;
it does not suppress manual dispatch, and skipped required checks can remain
pending. Record CI as deferred/skipped when used; do not bypass repository
protection or report skipped CI as green. See [GitHub's skip-workflow documentation](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/skip-workflow-runs).

Hosting publishes `release.json` with the source commit. Cloud Run stores the
same commit in the serving revision's `FF_RELEASE_SHA` environment variable.
Before either target is changed, the script requires the currently serving
commit to be an ancestor of the candidate. It also checks that the live release
has not changed during verification. An older or divergent candidate is blocked
unless `-AllowRollback -RollbackReason '...'` is supplied. That override is
recorded locally in `artifacts/releases/` along with the old and new platform
revision identifiers.

The API release publishes only missing composite indexes and unready explicit
single-field exemptions from the committed `firestore.indexes.json`. Existing
composite identities are excluded even while `CREATING` or `NEEDS_REPAIR`; they
are never submitted again as new indexes. A run-owned Firebase config references
only this subset. Both generated JSON files are hashed in the release record and
held with read-share locks until cleanup. Publication uses the pinned Firebase
CLI in noninteractive mode without `--force`, so definitions that exist only in
the live database are preserved. No index deletion or field-exemption removal is
part of a source release.

The manifest rejects a single ordered field plus its implicit same-direction
`__name__` order as a redundant composite. The investigation case listing uses
the default descending `createdAtUtc` index; the instance and pair-analysis
pages use default `caseId` and `status` indexes with document-name pagination.
Balance-transaction lookups by `roundId` or `idempotencyKey`, and Hi-Lo/House
Hold'em event lookups by `roundId`, likewise use default single-field indexes.
These query fields have no committed indexing exemption. The two-key
`caseId` + `auditId` evidence index remains a composite. Standalone release
verification checks this manifest before executing build commands.

The script then waits for every required composite index to reach Firestore's
`READY` state; a failed index still blocks release. Each exemption must
read back with no indexes, no inherited configuration and no reversion in
progress. Field overrides that define indexes and vector indexes remain blocked.
Before creating a non-serving Cloud Run revision, it also publishes the committed
`firestore.rules` if necessary and reads back the exact rules content. The script
records the previous and new rules releases, and stops if another publication
changes the rules during preparation. It checks the new revision's source stamp,
rechecks database prerequisites and the currently serving API revision immediately
before routing traffic to that exact revision.
The API image is built with `gcloud builds submit --ignore-file` using that
locked allowlist and a unique candidate tag. This supported command is used
instead of `run deploy --source`, whose installed SDK implementation cannot
accept an ignore-file override. `--suppress-logs` keeps remote build logs out
of the JSON result. Build status must be `SUCCESS`, with a valid build ID and
exactly one image result matching the requested tag and a valid SHA-256 digest.
The mutable tag is never resolved later. Cloud Run deploys only the image digest
returned in that build's result, with no traffic; its created revision must
read back that exact digest and the candidate source stamp. Build ID, requested
tag, immutable image reference/digest and upload manifest/ignore hashes are
saved in the release record.

The preconfigured Docker Artifact Registry repository `cloud-run-source-deploy`
in the release region is required; `-ArtifactRepository NAME` selects another
already configured repository. Its identity and format are checked. Missing
repositories or permissions stop the release; the script never creates registry
repositories or grants IAM permissions. CLI source archive packaging is a build
artifact from the existing checkout, not another repository or worktree.
Hosting freezes its verified built files and candidate stamp before the API
build begins. Only that captured manifest is streamed into a unique run-owned
build-artifact directory; no checkout or source repository is copied. Copied
bytes must reproduce the original manifest, then staged assets and the stamp
are held under read-share handles through publication. A generated, hashed,
read-locked Firebase config uses the absolute artifact directory, preserves the
candidate's headers/rewrites/original exclusions, and adds exact per-directory
ignore complements. Firebase14.4's glob implementation does not support ordinary
Git-style negation allowlists; tested negative-extglob complements exclude new
unmanifested siblings and directories instead. Unsupported filename segments or
oversized patterns refuse publication. The release record saves the staged
manifest and config hashes. A production smoke runs
afterward. A failed smoke does **not** silently roll back database changes or
traffic; inspect the recorded previous Firebase version, Firestore rules release
and Cloud Run revision before an explicit rollback. Source rollbacks keep existing
indexes and rules rather than reverting database security settings automatically.

## First guarded release when production is unstamped

If the live site has no recorded source commit, do not infer
its code from `HEAD`, `origin/main`, or a local `dist` folder. Preserve the
current Firebase Hosting version and Cloud Run revision, and compare the proposed
build with the actual live game bundles and behavior first. Use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/deploy.ps1 -InspectLive
```

This prints the live Hosting release/version, an index SHA-256 fingerprint, and
the serving API revision. An unstamped target blocks normal deployment. Once
the specific replacement build has been reviewed, the **one-time** adoption
requires the exact observed Hosting release and fingerprint, plus the API
revision:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/deploy.ps1 -AdoptUnstampedLive -ExpectedHostingRelease "RELEASE_FROM_INSPECTION" -ExpectedHostingIndexSha "SHA256_FROM_INSPECTION" -ExpectedApiRevision "REVISION_FROM_INSPECTION"
```

If releasing only one target, pass `-Target hosting` or `-Target api` and
only that target's expected fingerprint. The fingerprint check ensures the
unstamped live release has not changed between review and deployment; it does
**not** prove that the candidate contains every behavior from the unrecorded
release. Do not adopt while the current live/source mismatch is unresolved.

## What this does and does not guarantee

The guard prevents an older commit or an arbitrary dirty build from silently
replacing stamped production through this script. It cannot stop manual console
deployments, eliminate a tiny race between the last live check and platform
publication, guarantee that tests cover every game behavior, or make a database
schema rollback safe. Keep production access limited, do not use unguarded
deploy commands, and review the game-specific changes and smoke results.

Run the focused guardrail tests with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/deploy-guardrails.tests.ps1
```
