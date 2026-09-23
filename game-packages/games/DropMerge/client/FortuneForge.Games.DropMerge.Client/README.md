# `@fortuneforge/games-drop-merge`

Reusable 7×7 drop-and-merge React client and HTTP gateway. A randomized 2/4/8/16/32 box descends through a timed preview lane, then falls into the aimed column; the game tempo itself only ramps very slowly over time.

For a local preview:

```powershell
cd games/DropMerge/client/FortuneForge.Games.DropMerge.Client
npm install
npm run dev
```

Open `http://127.0.0.1:5192`. The development host uses the deterministic Drop Merge engine at `http://127.0.0.1:5202`.
