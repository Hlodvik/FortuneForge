# FortuneForge.Games.Mines

Server-only 25-tile Mines domain package. Boards use cryptographic selection without replacement;
the wager and mine count are fixed before board generation. Immutable round transitions do not
regenerate or move mines. `MinesRules.ReturnCents` settles exact rational payouts with one integer
floor, independently of display multiplier precision. See `../../RULES.md` for the complete math.

`MinesRoundState` contains the private board and is a persistence model. Return only
`MinesEngine.Project(state)` to a player. The host must atomically persist round state, a wallet
debit/credit, and command-idempotency results under an authenticated owner.
