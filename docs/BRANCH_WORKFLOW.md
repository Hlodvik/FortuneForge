# Branch and worktree workflow

`origin/main` is the canonical released history. A work branch owns one coherent change and one worktree. Do not use another task's worktree or leave finished work only in an uncommitted working directory.

## Starting work

1. Fetch and prune remote references: `git fetch --all --prune`.
2. Start the branch from current `origin/main` unless the task explicitly depends on another branch.
3. Use a separate worktree for concurrent work. Give both the branch and directory a descriptive name.
4. Commit a working checkpoint before switching tasks or handing the work to another person or agent.

Generated output belongs outside commits. In particular, `node_modules`, `dist`, `bin`, `obj`, and `.artifacts` directories are ignored.

## Integrating work

1. Run `./scripts/audit-worktrees.ps1` from any FortuneForge worktree.
2. Preserve meaningful uncommitted work in a named recovery branch before resolving conflicts.
3. Create a clean integration worktree from `origin/main`.
4. Cherry-pick focused feature commits from oldest to newest. Resolve conflicts by behavior, not by choosing an entire side of a file.
5. Keep repository-layout migrations separate from product changes. Rebase the migration onto the verified product branch; do not let an older mass move overwrite newer files.
6. Run the client tests, lint, production build, solution build, and server tests before updating `main`.

Current verification commands:

```powershell
Push-Location fortuneforge.client
npm ci
npm test
npm run lint
npm run build
Pop-Location

dotnet build FortuneForge.slnx --configuration Release
dotnet test FortuneForge.Server.Tests/FortuneForge.Server.Tests.csproj --configuration Release --no-build
```

Recovery branches prefixed with `salvage/` are evidence snapshots, not integration targets. Keep them until the consolidated branch has been reviewed and pushed. Then archive or delete them deliberately rather than merging them into `main`.
