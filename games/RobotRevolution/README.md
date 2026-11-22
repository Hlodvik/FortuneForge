# Sunstone Eclipse

Sunstone Eclipse is a five-reel temple slot with a game-owned Sunwheel feature. Its existing package directory, account keys, game ID, and paid API routes are retained so saved balances, free games, and already prepared rounds remain recoverable.

## Sunwheel Alignment

Three carved guardian rings trace separate five-position paths across the actual reel window: Jaguar, Eagle, and Serpent. Each landed Sunstone turns the calendar dial; the three rings rotate to the resulting positions. The displayed paths and awards are supplied by the settled server response.

On a guardian's path, its own symbol, a Sunstone, or a Wild counts as a match. A ring aligns with at least three matching positions. Three matches award 50% of the wager, four award 100%, and five award 200%. Every aligned ring's award is then multiplied by the number of aligned rings: one, two, or three. Each ring award rounds down to a whole wager point; a point is 25 cents.

If all three rings align while an actual Sunstone occupies the central heart cell, the full Eclipse awards another 2 times the wager. A Wild may help align a ring but cannot replace the Sunstone required in the heart. This is additional to ordinary payline and triple-sun pattern wins.

Three scatter gates award seven free Eclipse Spins. They retain the saved triggering wager and expand the window to six rows. During a free Eclipse Spin, Sunwheel ring awards and the full-Eclipse award are doubled. Existing free-spin banks and retriggers retain their authoritative server progression.

The extra Sunwheel award has a strict upper bound of 20 times the wager on a paid spin, or 40 times during a free spin. Normal payline and pattern wins are separate. There is no persistent collection meter, invented progress, client-side random wheel, side wager, or bonus purchase.

## Math and evidence

`SunstoneEclipse.cs` owns the geometry, point rounding, ring tiers and Eclipse award. `sunstone-eclipse-engine-v1` resolves these awards before wallet settlement and includes them in the payout breakdown as entries 1001, 1002, 1003 and 1010. Its `spinFeature` response describes the exact settled geometry and component awards. The native auditor reproduces the recorded random draws and rejects altered feature evidence.

The new base payline/pattern scale is 0.874. Historical `robot-revolution-engine-v1` rounds continue to use their frozen 1.126 scale and their original feature rules. An already prepared historical spin is replayed under its recorded version and does not acquire the new feature retrospectively.

Newly awarded Eclipse Spins use the existing valid compound mode `rows-arcade` as a game-owned version marker. Unmarked historical free-game carries, including `rows` carries and their retriggers, finish with the historical engine and payout rules. Counts and locked wagers are preserved; the neutral runtime and storage schema are unchanged.

Run the native paid-cycle qualification with:

```powershell
dotnet run --project tools/FortuneForge.SlotMath/FortuneForge.SlotMath.csproj --configuration Release -- 250000 --game robot-revolution-v1 --sunstone-new-only --sunstone-wagers=2,100 --sunstone-output=artifacts/sunstone-eclipse/math-qualification.json
```

The simulator includes earned free spins and all retriggers in their triggering paid cycle and retains pity and earned special-point state. The final new-only run qualifies both the minimum two-point wager and a 100-point wager with fixed seeds; the earlier historical engine baseline is reused. It reports both a never-use-boosts policy and a spend-earned-boosts-as-available policy. The observed release ceiling is 95%; simulation estimates and confidence bounds are not a certified RTP claim.
