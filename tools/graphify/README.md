# Graphify for Fortune Forge and Codex

The repository skill lives at `.agents/skills/graphify/SKILL.md`, Codex's current
repository discovery location. It uses [Graphify](https://github.com/Graphify-Labs/graphify)
through the wrapper here. The pinned Python dependency is in `requirements.txt`;
its isolated environment lives outside the checkout at
`%LOCALAPPDATA%\FortuneForgeTools\graphify`.

Run from the repository root in PowerShell:

```powershell
.\tools\graphify\graphify.ps1 status
.\tools\graphify\graphify.ps1 refresh
.\tools\graphify\graphify.ps1 query 'authentication session tokens'
.\tools\graphify\graphify.ps1 explain 'AccountSessionCookie'
.\tools\graphify\graphify.ps1 path 'useAuthenticatedAccount' 'useOptionalAccountSession'
```

On another computer, run `graphify.ps1 setup` first. It needs Python 3.11 through
3.14; pass `-PythonExe C:\path\to\python.exe` if Python is not discoverable.
Setup downloads the pinned package and its dependencies from PyPI. Subsequent
graph construction and queries use deterministic local parsing with LLM backends
disabled; no Graphify account or API key is required.

`refresh` indexes code, including C#, TypeScript and tests, using `.gitignore`
plus `excludes.txt`. Artwork, public assets, archives, build output, local captures
and installed skill files are excluded. It does not semantically index documents
or media. Graph data, reports, caches and query logs stay in the already ignored
`artifacts/graphify/` directory. Run `refresh` after source changes; a graph is a
snapshot, not a substitute for reading the cited source. Refresh uses Graphify's
incremental extraction when a prior graph exists.

Use `rg` first to locate text and files under the working agreements. Graphify
helps answer structural questions about callers, imports and relationships. Its
inferred edges require source verification, and missing edges do not prove that
a relationship is absent. This tool provides code navigation, not a security
audit or a compiler-verified call graph.
Some top-level files, including `FortuneForge.Server/Program.cs`, yield no named
symbols; inspect startup and dependency-injection wiring directly in source.
Prefer precise symbol names: broad terms such as "session" or "tokens" can also
match unrelated game and test nodes. `explain` and `path` give more focused results.

The installed skill is adapted from Graphify's pinned package to use this optional
local workflow. No Git hooks, root `AGENTS.md`, global Codex settings,
MCP server or hosted connection are installed. To make the skill visible, start
your next Codex turn; restart Codex if it is not listed.

To disable Codex discovery, remove only `.agents/skills/graphify/SKILL.md`.
The installed Python package and ignored index can remain; the PowerShell
wrapper will still work when called directly.
