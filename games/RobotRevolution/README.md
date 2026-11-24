# Sunstone Eclipse

Sunstone Eclipse is a five-reel, 23-payline temple slot with a game-owned Sunwheel feature. Its existing package directory, account keys, game ID, and paid API routes are retained so saved balances, free games, and already prepared rounds remain recoverable. New paid wagers start at 12 points (R3), advance in two-point steps, and stop at 300 points (R75). A point is 25 cents. Previously locked free-game wagers retain their historical range and math.

## Symbols and paytable

The five low symbols are Flame, Rain, Wind, Earth and Reed glyphs. The four picture symbols are Quetzal Headdress, Serpent, Eagle and Jaguar. The calendar image is the highest-paying Sunstone; the pointed Golden Sun is Wild. SOLAR_ECLIPSE and LUNAR_ECLIPSE appear only on the centre reel, with at most one Eclipse in any four-row window. One symbol starts its matching paid mode. Cacao, Earspool and the triple-sun pattern do not appear in new Sunstone Eclipse spins.

| Wire symbol | Three / four | Five |
| --- | ---: | ---: |
| GLYPH_FLAME | 1 | 3 |
| GLYPH_RAIN | 1 | 4 |
| GLYPH_WIND | 1 | 5 |
| GLYPH_EARTH | 1 | 6 |
| GLYPH_REED | 1 | 7 |
| FEATHER | 2 | 8 |
| SERPENT | 3 | 10 |
| EAGLE | 4 | 12 |
| JAGUAR | 5 | 16 |
| SUNSTONE | 8 | 32 |
| WILD | 6 (exactly three) | 24 |

These raw payline multipliers are scaled by 0.98 during ordinary play and 0.35 during Eclipse rounds, then rounded to whole points by the native engine. The enhanced Sunwheel supplies most of the Eclipse mode return. The guide labels ordinary values and provides separate Eclipse values. Wild substitutes in three- and five-symbol runs; an all-Wild run pays only at exactly three or five. The versioned strips and economics belong to `FifthSunTotalityDefinitions.cs`; `FifthSunDefinitions.cs` preserves historical v1 math.

## Sunwheel Alignment

Three carved guardian rings trace separate five-position paths across the actual reel window: Jaguar, Eagle, and Serpent. Each landed Sunstone turns the calendar dial; the three rings rotate to the resulting positions. The displayed paths and awards are supplied by the settled server response.

On a guardian's path, its own symbol, a Sunstone, or a Wild counts as a match. A ring aligns with at least three matching positions. Three matches award 50% of the wager, four award 100%, and five award 200%. Every aligned ring's award is then multiplied by the number of aligned rings: one, two, or three. Each ring award rounds down to a whole wager point; a point is 25 cents.

If all three rings align while an actual Sunstone occupies the central heart cell, the full Eclipse awards another 2 times the wager. A Wild may help align a ring but cannot replace the Sunstone required in the heart. This is additional to ordinary payline wins.

One centre-reel Eclipse starts five paid rounds locked to the triggering wager. Each round charges that wager once, pays ordinary line wins once, and resolves three distinct Sunwheel alignments over the same four-row board. Solar moves clockwise and Lunar anticlockwise. Eclipse symbols are absent from mode strips, so the feature cannot retrigger or overlap another Eclipse.

During an Eclipse round, awakened guardians remain lit and count toward the linked multiplier at subsequent alignments. A guardian must still match its current path to earn another award. All three awakenings award Totality once per round: 1× the wager, doubled by an actual heart Sunstone. Guardian memory resets at the next round. The current Eclipse guardian scale is 1.10 before point flooring, giving an Eclipse wheel-only maximum of 61.4× the wager; ordinary wheel maximum remains 20×. Normal line wins are separate. Both the remaining paid rounds and locked wager are saved on the server.

## Math and evidence

`fifth-sun-engine-v2` and `FifthSunTotalityWheel.cs` settle all three alignments before wallet settlement. The `spinFeature` response uses contract version 3, kind `fifth-sun`, math version `fifth-sun-totality-v1`. Guardian awards use entries 1001–1003, 1011–1013 and 1021–1023; Totality uses 1010. The public `paidMode` state is separate from historical free spins. Auditing replays recorded random draws and verifies both payout and paid-state evolution.

Historical `fifth-sun-engine-v1` rounds retain their 1.14 scale, three-scatter/seven-free-spin feature, six-row bonus and doubled wheel awards. Historical `sunstone-eclipse-engine-v1` rounds retain their classic symbols, 0.874 scale and triple-sun pattern. Older `robot-revolution-engine-v1` rounds retain their frozen 1.126 scale and original rules. Already prepared historical paid spins retain their recorded wager bounds and math version.

Existing `arcade-rows` carries finish under historical Sunstone Eclipse v1, `rows-arcade` under Sunstone v1, and older carries including `rows` under Robot v1. The shared store transports an optional opaque native-state document validated by this game; other games remain opted out. The original shared codec constructor remains available for prebuilt packages.

Run only the new native paid-cycle qualification with:

```powershell
$env:FIFTH_SUN_TOTALITY_MODE_SPINS='50000'
$env:FIFTH_SUN_TOTALITY_BASE_CYCLES='50000'
dotnet test games/RobotRevolution/tests/FortuneForge.Games.RobotRevolution.Tests --configuration Debug --filter FullyQualifiedName~FifthSunTotalityMathTests
```

The final qualification recorded 410,470 paid native outcomes at minimum and maximum stakes, including retained pity and both ordinary earned-boost policies. Solar mode estimated 205.17% at R3 and 210.19% at R75; Lunar estimated 211.74% and 216.99%. Every mode/stake lower approximate 95% confidence bound exceeded 200%. Complete paid cycles estimated 91.98–92.19%, with upper approximate 95% bounds below 94.06%. Mode and complete-cycle evidence are in `artifacts/fifth-sun/totality-mode-math-50000.json` and `totality-cycle-math-50000.json`. These are simulation estimates, not a certified theoretical RTP. Earlier wheel-scale-1 evidence is retained as superseded; historical math simulations were not repeated.
