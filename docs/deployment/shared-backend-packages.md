# Shared backend packages

One API service hosts the selected game backends. Each game keeps its own code,
complete published dependencies and isolated assembly loading. A deployment
restarts that shared process. Updating a game changes its selected immutable
package; it does not rebuild the other selected game packages.

`config/backend-packages.lock.json` is the committed selection authority. The
existing ignored local artifact store is
`artifacts/game-engine-releases/games/<gameId>/releases/<releaseId>/`. Here
`gameId` is the ID in the game's `game-release.json`; `releaseId` is the immutable
release UUID. `runtimeGameId` comes from the built module, including games whose
runtime identifier differs from their release configuration ID.

The package's `release.json` contains `contractVersion: 1`, `gameId`,
`runtimeGameId`, `releaseId`, `sourceSha`, `entry`, and `moduleType`. The staging
command obtains runtime identity and module type by running the published entry
with `--describe-backend`, which describes the package before creating an app or
accessing a database. Staging requires a clean committed source revision before
and after publishing so `sourceSha` identifies the actual source.

The lock has this structure; every published file, including `release.json`,
dependency manifests and native libraries, is listed with its exact byte length
and lowercase SHA256:

```json
{
  "contractVersion": 1,
  "packages": [
    {
      "gameId": "jade-dragon",
      "runtimeGameId": "empty-slots-v1",
      "releaseId": "00000000-0000-0000-0000-000000000001",
      "sourceSha": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
      "entry": "FortuneForge.Games.PackagedGame.Server.dll",
      "moduleType": "FortuneForge.Games.PackagedGame.Server.GameBackendPackage",
      "files": [
        {
          "path": "FortuneForge.Games.PackagedGame.Server.dll",
          "size": 123,
          "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        }
      ]
    }
  ]
}
```

The example file inventory is abbreviated. Real selections require the complete
publish tree and the entry's `.deps.json` and `.runtimeconfig.json`.

## Updating one game

Commit the intended source, then stage only that game:

```powershell
node scripts/deploy-game.mjs jade-dragon --backend-only --stage
node scripts/shared-backend-packages.mjs pin jade-dragon <new-release-UUID>
node scripts/shared-backend-packages.mjs verify
```

`pin` validates the staged package and atomically replaces only that game's
selection. Other package selections remain unchanged. A selected UUID cannot be
repinned with different bytes. Review and commit the changed lock, and retain
all selected artifact directories. The lock alone cannot recreate old binaries:
another checkout or build machine must receive those exact verified directories
before deploying. Changing a source file does not implicitly change the selected
game package.

The main API deployment publishes its own host, then materializes the selected
packages into that fresh publish directory before building the single image:

```powershell
node scripts/shared-backend-packages.mjs materialize --output <main-api-publish-directory>
node scripts/shared-backend-packages.mjs verify-materialized --output <main-api-publish-directory>
```

Output contains `game-packages/composition.json` and complete folders at
`game-packages/<runtimeGameId>/<releaseId>/`. DLLs are never merged into the host's
publish root. Verification checks all selected identities, file sets, sizes and
hashes; extra, missing, changed, redirected or case-colliding files fail. The lock
is checked again before completing materialization. Existing output package
directories are preserved and cause a failure; use a fresh publish directory.

`--manifest <path>` and `--store <path>` override the lock and local artifact-store
roots for all commands. These tools perform no cloud deployment or database
changes. Their exported functions support callers that already manage a release
workflow: `stageBackendPackage`, `pinBackendPackage`,
`verifyBackendComposition`, `materializeBackendComposition`, and
`verifyMaterializedBackendComposition`. Existing output verification needs only
the lock and materialized directory, so the source store can be offline. It
checks the canonical `composition.json` bytes as well as every selected artifact.
Verification and materialization report `manifestSha256` for the raw input lock
bytes and `compositionSha256` for the canonical materialized composition bytes.

## Runtime ownership

The shared host must load each complete selected publish directory in its own
assembly context and preserve per-game dependency injection and configuration.
Wallet, batch wallet and roster adapters must remain bound to their game's
runtime identity. The platform retains account authentication, authorization,
request auditing and money validation. The host also owns starting, stopping and
disposing each package's recovery services once; constructing packages alone
does not start their hosted services.
