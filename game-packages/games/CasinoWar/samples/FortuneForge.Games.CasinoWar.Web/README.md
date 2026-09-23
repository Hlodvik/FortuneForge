# Casino War local web adapter

Developer-only ASP.NET adapter for the reusable Casino War client contract. Each round uses a cryptographically Fisher–Yates-shuffled six-deck shoe in the host and delegates the opening, War, and Tie side-bet rules to the Casino War package.

The returned `roundId` is a local play and audit correlation ID. Authentication and durable persistence belong to the integrating host.

Run the API:

```powershell
dotnet run --project games/CasinoWar/samples/FortuneForge.Games.CasinoWar.Web
```

The API is available at `http://127.0.0.1:5188/api/games/casino-war`.
