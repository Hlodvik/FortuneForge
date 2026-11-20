---
name: graphify
description: Use when the user explicitly asks to map or query Fortune Forge code relationships with Graphify.
---

# Graphify for Fortune Forge

Graphify is optional navigation support. User and repository working agreements
prevail. Reach first for `rg` or `rg --files` when locating text or files. Use
Graphify when a graph of callers, imports or relationships would help; ordinary
coding tasks do not require it.

Use the repository PowerShell wrapper, which locates the isolated pinned Python
environment without relying on a global `graphify` command:

```powershell
.\tools\graphify\graphify.ps1 status
.\tools\graphify\graphify.ps1 refresh
.\tools\graphify\graphify.ps1 query 'authentication session tokens'
.\tools\graphify\graphify.ps1 explain 'AccountSessionCookie'
.\tools\graphify\graphify.ps1 path 'FirstSymbol' 'SecondSymbol'
```

Resolve `tools/graphify/graphify.ps1` from the repository root when working in a
subdirectory. Check `status` and refresh after source changes before relying on
relationships. The index is a snapshot of the local checkout, and the wrapper
puts it under ignored `artifacts/graphify/`, not `graphify-out/`.

The wrapper forces code-only extraction with all LLM backends disabled. It
indexes C#, TypeScript and other supported code while skipping documents/media,
build output, artwork and archived captures. See `tools/graphify/README.md` for
setup and `tools/graphify/excludes.txt` for the exact exclusions. Do not use the
upstream full semantic extraction workflow, install hooks, change global Codex
settings or create another checkout as part of this local navigation setup.

Treat graph edges as leads. Read source before editing, distinguish explicit
edges from inferred ones, and validate behavior with appropriate tests. Missing
edges do not establish that no call exists. Dependency injection, reflection,
runtime configuration and dynamic TypeScript can leave relationships incomplete.
This graph is not a security scan or a compiler-verified call graph.

Upstream: https://github.com/Graphify-Labs/graphify
