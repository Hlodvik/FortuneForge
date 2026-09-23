# Baccarat local web adapter

Developer-only ASP.NET adapter for the reusable Baccarat client contract. Each hand uses a cryptographically Fisher–Yates-shuffled eight-deck shoe in the host and delegates dealing and settlement to the Punto Banco package.

The `roundId` returned for each hand is the local play and audit correlation ID, providing a seam for a later durable integration. Persistence and authentication belong to the integrating host.

Run the API:

```powershell
dotnet run --project games/Baccarat/samples/FortuneForge.Games.Baccarat.Web
```

The API is available at `http://127.0.0.1:5187/api/games/baccarat`.
