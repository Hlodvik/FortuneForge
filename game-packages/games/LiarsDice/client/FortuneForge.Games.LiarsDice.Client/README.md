# Fortune Forge Liar's Dice client

Reusable React UI for exact-face Liar's Dice. The local preview runs one human player against three deterministic bots, with visible personal dice, bid raising, challenges, dice elimination, and next-round progression.

```powershell
npm install
npm run dev
```

Open http://127.0.0.1:5180. Players start with five dice; the last player with dice wins.

## Result-driven dice animation

The exported `DiceThrow` component accepts the authoritative result as `values`, a changing `rollKey`, and optional `{ x, y, rotation }` landing positions. Four 18-frame sprite sheets have distinct center-origin, multi-axis tumbles with visible side faces, contacts, rebounds, and stopping times. Every sheet contains all six result rows, making the authoritative face the final rendered orientation. The sheets cycle automatically for five- and six-die hands.

Checks:

```powershell
npm run check
npm test
npm run build
```

The preview has no production identity, account, payment, or ledger integration.
