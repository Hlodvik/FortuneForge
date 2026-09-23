# Craps

This module implements the Pass-Line flow for Craps.

Run tests (shared test project, filter to Craps):

```bash
dotnet test FortuneForge.Games.slnx --filter FullyQualifiedName~CrapsEngineTests
```

Design: `CrapsEngine` models the pass-line bet lifecycle with `CrapsPassLineState` and returns `CrapsTransition` objects representing state transitions and outcomes.
