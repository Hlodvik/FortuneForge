# Release safety

`main` is the only release history. A deployment selects a commit on that history,
checks it out temporarily without creating a branch, and builds from that clean
checkout. Edits in the normal working tree stay local. A push does not deploy.

## Normal release

After committing the intended change to `main`, run `npm run deploy`, or use
`npm run deploy:hosting` / `npm run deploy:api`. To deploy an earlier commit on
the same main history, call `scripts/deploy.ps1 -Commit SHA` explicitly. The
Firebase commands use the active gcloud account, which must also already be
signed in to Firebase CLI, so the two tools cannot silently choose different
default users. The
active deploy script, guard module, guard tests, and release verifier must match
their committed versions on the current local `main` HEAD. Uncommitted release
tooling blocks deployment, but unrelated local game edits may remain uncommitted.
The script then runs `npm ci` and current main's committed release verifier
against the selected immutable checkout, plus the focused committed deployment
guard tests, before touching production. The selected source HEAD and clean
status are checked before and after verification and before publication. This
also supports explicitly selected older application commits without copying new
tooling into their source trees. The 250,000-spin slot Monte Carlo job is **not** part of every
deployment. Run `npm run verify:slot-math` separately when slot math changes.

### Automatic visual-only verification

For Hosting-only releases, the guard compares the actual live Hosting source
stamp with the complete selected candidate tree, not just its latest commit.
Exactly one registered game's CSS, image/font assets (including SVG), and
colocated tests/docs may qualify. At least one visual change is required.
Application TypeScript/TSX, gateways, wallet/settlement code, manifests, lockfiles,
configuration, shared files, backend code, multiple games, deletions/renames,
unusual file modes, or any uncertainty use the full suite. API/combined releases,
rollbacks, adoption, and missing/unavailable baselines always use the full suite.
There is no force-scoped override.

Qualified releases run the affected game's checks/tests, every game build,
shared web tests, lint/build, ownership/route checks, release guardrails and the
normal full production smoke. Only unrelated game checks/tests and .NET tests
are skipped. Clean checkouts still need every game built for package exports.
The verifier independently recalculates the plan. The release record saves the
baseline, candidate, tooling SHA, mode, workspace, reason, and changed paths.
Standalone `npm run verify:release` defaults to the full suite.

### Explicit Hosting test-suite waiver

When the owner explicitly requests deployment without application test suites,
use `scripts/deploy.ps1 -Target hosting -SkipTestSuites -TestWaiverReason 'Owner requested skipping test suites for the reviewed visual change.'`.
Both flags are required together, and API/combined releases reject a waiver
before reading live services. This is not a force-scoped override: the live-to-candidate
plan is still independently calculated and may remain `full`. Only application
npm test suites and .NET test runs are omitted. Type checks, every game build,
lint, web build, ownership/route checks, focused deployment guard tests, immutable
source/main/ancestry/live-race checks, release stamps and production smoke remain.
The release record stores `testSuiteWaiver.skipped` and its reason; output says
application tests were **NOT RUN**, never that they passed. Standalone verification
does not accept this waiver.

Hosting publishes `release.json` with the source commit. Cloud Run stores the
same commit in the serving revision's `FF_RELEASE_SHA` environment variable.
Before either target is changed, the script requires the currently serving
commit to be an ancestor of the candidate. It also checks that the live release
has not changed during verification. An older or divergent candidate is blocked
unless `-AllowRollback -RollbackReason '...'` is supplied. That override is
recorded locally in `artifacts/releases/` along with the old and new platform
revision identifiers.

The API release reads production first, writes a temporary Firebase config with
only missing composite indexes and unready explicit single-field exemptions from
the committed `firestore.indexes.json`, and publishes that subset without `--force`.
Firebase CLI's noninteractive mode leaves other live indexes and exemptions in
place. The release then waits for every composite index in the full committed
definition to reach Firestore's `READY` state. It does not POST an index that is
already `CREATING` or `READY`; a failed existing index blocks on the readiness check.
The temporary config is removed after the attempt. Each exemption must
read back with no indexes, no inherited configuration and no reversion in
progress. Field overrides that define indexes and vector indexes remain blocked.
Before creating a non-serving Cloud Run revision, it also publishes the committed
`firestore.rules` if necessary and reads back the exact rules content. The script
records the previous and new rules releases, and stops if another publication
changes the rules during preparation. It checks the new revision's source stamp,
rechecks database prerequisites and the currently serving API revision immediately
before routing traffic to that exact revision.
Hosting publishes the verified build with its stamp. A production smoke runs
afterward. A failed smoke does **not** silently roll back database changes or
traffic; inspect the recorded previous Firebase version, Firestore rules release
and Cloud Run revision before an explicit rollback. Source rollbacks keep existing
indexes and rules rather than reverting database security settings automatically.

## First guarded release from today's unstamped production

The current live site was not built from a recorded source commit. Do not infer
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
