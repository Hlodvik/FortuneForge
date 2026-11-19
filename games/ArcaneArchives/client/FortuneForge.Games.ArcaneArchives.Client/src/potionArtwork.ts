// Edited: 2026-11-18T18:18:18-06:00 | Computer: VINNABOK
import mandrakeBottle from './assets/art/potions/rootwake-potion-v1.webp'
import mandrakeEffect from './assets/art/potions/mandrake-cabinet-fx-v1.webp'
import nightshadeBottle from './assets/art/potions/nightveil-potion-v1.webp'
import nightshadeEffect from './assets/art/potions/nightshade-cabinet-fx-v1.webp'
import mooncapBottle from './assets/art/potions/moonfall-potion-v1.webp'
import mooncapEffect from './assets/art/potions/mooncap-cabinet-fx-v1.webp'
import phoenixBottle from './assets/art/potions/emberheart-potion-v1.webp'
import phoenixEffect from './assets/art/potions/phoenix-cabinet-fx-v1.webp'
import wolfsbaneBottle from './assets/art/potions/wolfsbane-draught-potion-v1.webp'
import wolfsbaneEffect from './assets/art/potions/wolfsbane-cabinet-fx-v1.webp'
import quicksilverBottle from './assets/art/potions/silverflux-potion-v1.webp'
import quicksilverEffect from './assets/art/potions/quicksilver-cabinet-fx-v1.webp'
import mandrakeBottling from './assets/art/potions/mandrake-bottling-v1.webp'
import mandrakeActivation from './assets/art/potions/mandrake-activation-v1.webp'
import nightshadeBottling from './assets/art/potions/nightshade-bottling-v1.webp'
import nightshadeActivation from './assets/art/potions/nightshade-activation-v1.webp'
import mooncapBottling from './assets/art/potions/mooncap-bottling-v1.webp'
import mooncapActivation from './assets/art/potions/mooncap-activation-v1.webp'
import phoenixBottling from './assets/art/potions/phoenix-bottling-v1.webp'
import phoenixActivation from './assets/art/potions/phoenix-activation-v1.webp'
import wolfsbaneBottling from './assets/art/potions/wolfsbane-bottling-v1.webp'
import wolfsbaneActivation from './assets/art/potions/wolfsbane-activation-v1.webp'
import quicksilverBottling from './assets/art/potions/quicksilver-bottling-v1.webp'
import quicksilverActivation from './assets/art/potions/quicksilver-activation-v1.webp'

type PotionArtwork = Readonly<{
  bottle: string
  effect: string
  glow: string
  bottling: string
  activation: string
}>

const artwork: Readonly<Record<string, PotionArtwork>> = {
  MANDRAKE_ROOT: { bottle: mandrakeBottle, effect: mandrakeEffect, glow: '#baf6a0', bottling: mandrakeBottling, activation: mandrakeActivation },
  NIGHTSHADE_BERRIES: { bottle: nightshadeBottle, effect: nightshadeEffect, glow: '#d5b8ff', bottling: nightshadeBottling, activation: nightshadeActivation },
  MOONCAP_MUSHROOM: { bottle: mooncapBottle, effect: mooncapEffect, glow: '#dbfcff', bottling: mooncapBottling, activation: mooncapActivation },
  PHOENIX_FEATHER: { bottle: phoenixBottle, effect: phoenixEffect, glow: '#ffe3a0', bottling: phoenixBottling, activation: phoenixActivation },
  WOLFSBANE_WINE: { bottle: wolfsbaneBottle, effect: wolfsbaneEffect, glow: '#ffbbe5', bottling: wolfsbaneBottling, activation: wolfsbaneActivation },
  QUICKSILVER: { bottle: quicksilverBottle, effect: quicksilverEffect, glow: '#e9ffff', bottling: quicksilverBottling, activation: quicksilverActivation },
}

export function getPotionArtwork(ingredientId: string): PotionArtwork | undefined {
  return Object.hasOwn(artwork, ingredientId) ? artwork[ingredientId] : undefined
}
