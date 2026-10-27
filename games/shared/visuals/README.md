# Shared casino materials

`emerald-felt-v2.webp` is the approved borderless Texas Hold’em felt, reused byte-for-byte without regenerating, recoloring or enlarging it (1586 × 992, about 76 KB). Its original generation prompt and provenance remain in `games/TexasHoldem/client/FortuneForge.Games.TexasHoldem.Client/src/assets/ARTWORK.md`.

Import `casino-felt.css` and use `background: var(--ff-casino-felt-material)` on the existing green playing surface. The shared material includes the same subtle dark tint as the approved Hold’em table. Layout, printed markings, foreground artwork and rails belong to each game; do not apply the material to non-green themed games or individual buttons.

The app imports the stylesheet for legacy card pages. Packaged clients import it through a relative CSS import, so their independent previews and built styles use the same source image without depending on a different game or the host application. The earlier Hold’em-local image is retained as historical artwork but is no longer the runtime source.
