# Branch consolidation — 2026-09-22

## Verified product branch

The clean integration branch is `codex/consolidated-20260922`, based on `origin/main` at `dc6c564`.

It contains:

- the latest active workspace snapshot preserved as `salvage/active-latest-20260922` (`b2391ef`);
- the independent game-feel work preserved as `salvage/game-feel-20260922` (`91b0b07`);
- conflict resolutions that keep the current game assets, routes, controls, special rounds, and package revisions while using one shared outcome presentation instead of parallel old and new result panels;
- current Horse Flight and Snake client packages with recorded SHA-256 checksums; and
- the branch/worktree workflow and read-only audit script.

The original checkout is clean. Generated `.artifacts` directories and 64 obsolete, untracked client package archives were removed after the meaningful workspace was committed. They were build/package output and can be regenerated; they were never part of a branch.

## Layout migration held separately

`salvage/monorepo-layout-20260922` (`c095a7e`) remains preserved but is intentionally not part of the verified product branch.

That commit is based directly on the older `origin/main` snapshot and contains 704 detected renames, 22 additions, and an alternate permanent bot-account and analytics architecture. Applying it after the verified product commits produces widespread rename/location conflicts across newer routes, assets, slot implementations, server tests, and the current configured bot directory. Merging it mechanically would recreate the stale-code regression this consolidation is meant to stop.

Treat the layout work as a separate migration. Rebuild it from `codex/consolidated-20260922`, move one boundary at a time, and keep its permanent bot-account design behind an explicit architecture decision and dedicated tests.

## Verification

- Client tests: 218 passed, 0 failed.
- Client lint: passed with one existing Fast Refresh warning in `GameAmbientMusic.tsx`.
- Client production build and catalog verification: passed.
- .NET Release solution build: passed with zero errors.
- Server tests: 424 passed, 0 failed.

The .NET build reports two moderate advisories inherited from the current Vitest dependency chain. No automatic dependency or audit fix was applied during consolidation.
