// Edited: 2026-11-18T18:18:18-06:00 | Computer: VINNABOK
import type { SlotPotionBottle } from './arcaneFeatures'
import mandrakeBottle from './assets/art/potions/rootwake-potion-v1.webp'
import nightshadeBottle from './assets/art/potions/nightveil-potion-v1.webp'
import mooncapBottle from './assets/art/potions/moonfall-potion-v1.webp'
import phoenixBottle from './assets/art/potions/emberheart-potion-v1.webp'
import wolfsbaneBottle from './assets/art/potions/wolfsbane-draught-potion-v1.webp'
import quicksilverBottle from './assets/art/potions/silverflux-potion-v1.webp'
import mandrakeBottling from './assets/art/potions/mandrake-bottling-v1.webp'
import nightshadeBottling from './assets/art/potions/nightshade-bottling-v1.webp'
import mooncapBottling from './assets/art/potions/mooncap-bottling-v1.webp'
import phoenixBottling from './assets/art/potions/phoenix-bottling-v1.webp'
import wolfsbaneBottling from './assets/art/potions/wolfsbane-bottling-v1.webp'
import quicksilverBottling from './assets/art/potions/quicksilver-bottling-v1.webp'
import nightshadeEmpty from './assets/art/potions/arcane-intro-empty-nightshade.webp'
import mooncapEmpty from './assets/art/potions/arcane-intro-empty-mooncap.webp'
import phoenixEmpty from './assets/art/potions/arcane-intro-empty-phoenix.webp'
import wolfsbaneEmpty from './assets/art/potions/arcane-intro-empty-wolfsbane.webp'
import quicksilverEmpty from './assets/art/potions/arcane-intro-empty-quicksilver.webp'
import astralBottle from './assets/art/potions/rare-astral-convergence.webp'
import astralEmpty from './assets/art/potions/rare-astral-convergence-empty.webp'
import astralRim from './assets/guide/guide-astral-convergence-still.webp'
import runeboundBottle from './assets/art/potions/rare-runebound-elixir.webp'
import runeboundEmpty from './assets/art/potions/rare-runebound-elixir-empty.webp'
import runeboundRim from './assets/guide/guide-runebound-elixir-still.webp'
import { GUIDE_ASSETS } from './guideAssets'

export type PotionArtwork = Readonly<{
  bottle: string
  empty: string
  rim?: string
  rimStill: string
  glow: string
  bottling?: string
  mouth: readonly [x: number, y: number, radius: number]
  drain: readonly [top: number, bottom: number]
  liquid: readonly [shadow: string, midtone: string, highlight: string]
}>

function artworkFor(slug: string, art: Omit<PotionArtwork, 'rim' | 'rimStill'>): PotionArtwork {
  return {
    ...art,
    rim: GUIDE_ASSETS[`assets/guide-${slug}-idle.webp`],
    rimStill: GUIDE_ASSETS[`assets/guide-${slug}-still.webp`],
  }
}

const artwork: Readonly<Record<string, PotionArtwork>> = {
  MANDRAKE_ROOT: artworkFor('mandrake', {
    bottle: mandrakeBottle, empty: GUIDE_ASSETS['assets/arcane-intro-empty-mandrake.webp'],
    glow: '#baf6a0', bottling: mandrakeBottling, mouth: [112, 36, 19], drain: [.301, .924],
    liquid: ['#344509', '#92b94b', '#e4ffd2'],
  }),
  NIGHTSHADE_BERRIES: artworkFor('nightshade', {
    bottle: nightshadeBottle, empty: nightshadeEmpty, glow: '#d5b8ff', bottling: nightshadeBottling,
    mouth: [113, 44, 13], drain: [.426, .959], liquid: ['#410751', '#9335a4', '#e0b6ea'],
  }),
  MOONCAP_MUSHROOM: artworkFor('mooncap', {
    bottle: mooncapBottle, empty: mooncapEmpty, glow: '#dbfcff', bottling: mooncapBottling,
    mouth: [113, 62, 17], drain: [.393, .971], liquid: ['#196286', '#58b8d5', '#c2faff'],
  }),
  PHOENIX_FEATHER: artworkFor('phoenix', {
    bottle: phoenixBottle, empty: phoenixEmpty, glow: '#ffe3a0', bottling: phoenixBottling,
    mouth: [113, 44, 12], drain: [.390, .953], liquid: ['#922e1c', '#dd672d', '#ffd187'],
  }),
  WOLFSBANE_WINE: artworkFor('wolfsbane', {
    bottle: wolfsbaneBottle, empty: wolfsbaneEmpty, glow: '#ffbbe5', bottling: wolfsbaneBottling,
    mouth: [113, 33, 15], drain: [.312, .694], liquid: ['#48142f', '#903652', '#f1b6bd'],
  }),
  QUICKSILVER: artworkFor('quicksilver', {
    bottle: quicksilverBottle, empty: quicksilverEmpty, glow: '#e9ffff', bottling: quicksilverBottling,
    mouth: [103, 48, 17], drain: [.363, .961], liquid: ['#414956', '#929cac', '#fffdfd'],
  }),
  'astral-convergence': {
    bottle: astralBottle, empty: astralEmpty, rimStill: astralRim, glow: '#92b7ff',
    mouth: [112, 22, 18], drain: [.40, .86], liquid: ['#5622bd', '#395aff', '#56f8eb'],
  },
  'runebound-elixir': {
    bottle: runeboundBottle, empty: runeboundEmpty, rimStill: runeboundRim, glow: '#ffc177',
    mouth: [111, 22, 17], drain: [.42, .85], liquid: ['#bb2766', '#eeb33c', '#63ed93'],
  },
}

export function getPotionArtwork({ ingredientId, variantId }: Pick<SlotPotionBottle, 'ingredientId' | 'variantId'>): PotionArtwork | undefined {
  if (variantId && Object.hasOwn(artwork, variantId)) return artwork[variantId]
  return Object.hasOwn(artwork, ingredientId) ? artwork[ingredientId] : undefined
}
