# Craps console sample

This developer-only sample exercises the production `FortuneForge.Games.Craps` pass-line state machine without turning the game library into a public API host.

Run a representative round:

```powershell
dotnet run --project games/Craps/samples/FortuneForge.Games.Craps.Console -- --player player --stake 10 --roll 2,2 --roll 2,3 --roll 1,3
```

The rolls establish point 4, continue with no decision, then hit 4 and resolve the pass-line bet. Use additional repeated `--roll FIRST,SECOND` arguments for another deterministic sequence.
