# Visual reference evaluation — October 20, 2026

The 17-test core-engine gate passed before these digital game screens were inspected. These are
actual rendered game interfaces, not marketing thumbnails or live-dealer footage.

| Product | Viewed screen | Observed design | Decision |
| --- | --- | --- | --- |
| Stake Mines | [Game screenshot](https://files.bo3.gg/uploads/image/79352/image/webp-882f39db98959b87dcf531fa29deac5a.webp), [source page](https://bo3.gg/games/articles/mines-gambling-game-honest-review), [official game](https://stake.com/casino/games/mines?game=mines) | Left controls; compact wager and half/double buttons; 5×5 grid fills right; raised blue slate tiles; recessed safe tiles with luminous green gems; bright full-width cash-out; mine/gem counters and current multiplier | **Chosen layout and material reference.** Direct, readable and well suited to single-player FortuneForge. Official live page displayed a region notice; no attempt to play or bypass it. |
| Spribe Mines | [Game screenshot](https://mines-slot.com/assets/images/8.webp), [source](https://mines-slot.com/) | Centered grid; blue field and rounded insets; yellow safe stars; mine selector/next quote above grid; wager and cash-out bar below | Viewed and compared. Bottom-control arrangement is less suitable for matching FortuneForge desktop chrome; next-multiplier visibility is useful. |
| Hacksaw Mines | [Game screenshot](https://slotcatalog.com/userfiles/image/games/Hacksaw-Gaming/24304/Mines-Hacksaw-Gaming-6891946.jpg), [source](https://slotcatalog.com/en/slots/Mines-Hacksaw-Gaming) | Purple field; raised square tiles; glowing cyan safe tiles with cut gems; multiplier progression above; balance/bet/collect in bottom strip | Viewed and compared. Clear progression, but the bottom casino console and large title spend more space on chrome. |

Implementation closely follows the selected left-controls/right-board structure and square tile
geometry. A generated slate texture supplies tactile material; original generated emerald and
graphite/copper mine sprites replace provider artwork. Safe reveals use a short scale/sparkle-like
glow, a mine uses a brief shake and red recess, and cash-out uses a centered result plaque with the
entire fixed board revealed behind it. Unchosen terminal tiles are subdued. Reduced-motion settings
remove all animations. This review observed still screens; these specific animation timings are
FortuneForge's own treatment rather than claims about timing measured from provider playback.

Small portrait moves the board above compact controls; short landscape preserves two columns.
Phone 320×568 retains 45px tile targets and first-viewport cash-out. Real shared `InGameShell`
provides FortuneForge home and game-library exits in the preview. Host integration uses that same
shell through the normal route.

External screenshots were only viewed through the browser and are linked for review. No external
provider assets, logos, sounds or screenshot pixels are bundled in this package.
