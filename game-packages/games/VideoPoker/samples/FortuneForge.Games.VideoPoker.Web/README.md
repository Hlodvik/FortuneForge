# Video Poker local web adapter

Developer-only ASP.NET host for the Video Poker client contract. It keeps local free-play rounds and a 1,000-credit balance in memory, uses a cryptographically shuffled standard deck in the host, and delegates deal, draw, evaluation, and settlement to `VideoPokerRoundEngine`.

Run the API:

```powershell
dotnet run --project games/VideoPoker/samples/FortuneForge.Games.VideoPoker.Web
```

The API is available at `http://127.0.0.1:5186/api/games/video-poker`.
