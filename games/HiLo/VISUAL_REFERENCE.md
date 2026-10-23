# Hi-Lo digital reference review

The pure engine passed its first 34 tests **before** digital-screen review began on October 20, 2026. The final engine suite contains 35 tests, including independent rational payout checks. Reference inspection used real browser screenshots of provider-published digital game screens, rather than relying on descriptions or live-dealer footage.

## Inspected screens

1. [Hacksaw Gaming Hi-Lo](https://www.hacksawgaming.com/games/hi-lo), including its [published actual digital screen](https://www-live.hacksawgaming.com/casino_thumbnails/1328_hero_Device_Desktop.png). Viewed full screen in the browser. It has a narrow bet sidebar, indigo patterned playing surface, oversized white central card, directional controls on either side, small card trail and a green collect control below. This is the **chosen primary reference**.
2. [Spribe HiLo](https://spribe.co/games/hi-lo). Viewed the provider's in-game screenshot in its page carousel: amber table, large center card and blue-backed shoe, history across the top, pill-shaped prediction controls and bottom cashout. Its demo asks for age verification, so interactive access stopped there. The visible digital screenshot was sufficient for comparison.
3. [Stake Hilo](https://stake.com/casino/games/hilo). Viewed the actual rendered game layout: bet sidebar, main white card, skip control, odds and profit fields, history below. A regional availability modal subsequently covered the surface; no attempt was made to bypass it or sign in. Its interface is more utilitarian than the selected reference.

Also opened BGaming's Hi-Lo Switch product page, but an age gate prevented meaningful screen inspection; it is **not counted** as a visually verified reference. Its official published rules were used only as one rules reference, with FortuneForge's variant differences explicitly documented.

## Translation into FortuneForge

The implementation closely follows Hacksaw's spatial composition: bet/wallet sidebar, raised central white card, adjacent arrow controls with odds, revealed-card trail and green collect control. The indigo cloth, card back, typography, icon shapes and FortuneForge identity are original. The screen is adapted to two strict prediction choices, and includes exact payable returns plus the current multiplier. The chosen product's additional color bets and same-rank rules are not part of this variant.

The initial card is face down until the wager is accepted by the server. A short perspective reveal animates new cards; history and return update only from accepted server snapshots. Reduced motion removes the animation. Desktop keeps the full table in one viewport. Phones preserve the central play surface and move wager controls to the bottom. Short landscape uses a compact sidebar and a separate collect/history column.

No provider logo, extracted game artwork, downloaded proprietary sprites, copied sound or live-dealer footage is used.

## Original raster material

Generated with the built-in **imagegen** tool. Original project copy: `client/FortuneForge.Games.HiLo.Client/src/assets/indigo-cloth.png`. The runtime version is `indigo-cloth.webp` in the same folder; it was format-encoded at WebP quality 85 to reduce the material from 3.60 MB to 413 KB without changing dimensions or composition. The card faces and geometric card back are native code, following the shared FortuneForge card-rank/suit conventions.

Final generation prompt:

> Use case: product-mockup. Asset type: seamless-appearing background texture for a premium digital casino card table. Primary request: an original dark midnight indigo woven casino cloth material, viewed perfectly flat from directly overhead, filling the entire rectangular image. Fine small diamond jacquard weave with authentic textile fibres, extremely restrained navy and blue violet contrast. A broad soft pool of cool light near the upper center and deep dark edges, subtle diagonal sheen, no objects. Palette nearly black indigo #101329 through desaturated blue violet #272c55, no bright gold. Composition landscape 1536x1024, all cloth, no table edge, no cards, no text, no logos, no symbols, no border, no watermark. The final material will sit behind crisp white playing cards and controls, so keep it calm and dark. Photoreal macro material finish, not illustration, not a UI mockup.

Approved card/chip sounds come from the repository's shared `games/assets/audio/cards` set. Copies retain the CC0 license in the client assets/audio folder. Sora uses the declared fontsource package. No external artwork was extracted from the inspected references.
