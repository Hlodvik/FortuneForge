import { createSlotBackdropSvg, createSlotIconSvg } from '../shared/themedSvg'

const palette = [
  ['#6a8f32', '#243b1f', '#f2c664', '#a7dd55'],
  ['#b85a27', '#4b2418', '#f7d06b', '#ff8a47'],
  ['#347ba1', '#133849', '#f3c85d', '#63ccec'],
  ['#8a5d33', '#362719', '#f2ce77', '#d99b5d'],
] as const

function icon(label: string, glyph: string, variant: number): string {
  const [background, backgroundDeep, rim, glow] = palette[variant % palette.length]
  return createSlotIconSvg({ label, glyph, background, backgroundDeep, rim, glow })
}

export const DINO_DOMINION_VISUALS = {
  backdrop: createSlotBackdropSvg({
    label: 'Dino Dominion prehistoric fossil valley',
    motif: '🦖',
    skyTop: '#173e45',
    skyBottom: '#cf7432',
    horizon: '#3f6130',
    accent: '#ffc75f',
    ground: '#271d16',
  }),
  emblem: icon('Dino Dominion emblem', '🦖', 0),
  accent: icon('Amber fossil accent', '🟠', 1),
  bone: icon('Ancient fossil bone', '🦴', 0),
  egg: icon('Speckled dinosaur egg', '🥚', 1),
  fern: icon('Primeval fern', '🌿', 2),
  footprint: icon('Giant dinosaur footprint', '🐾', 3),
  raptor: icon('Swift raptor', '🦎', 0),
  rex: icon('Mighty tyrannosaurus', '🦖', 1),
  wild: icon('Triceratops wild crest', '🦕', 2),
  free: icon('Volcanic cave free game', '🌋', 3),
  power: icon('Golden amber power stone', '🟠', 0),
  energy: icon('Falling meteor charge', '☄️', 1),
  lineBonus: icon('Fossil claw trio', '🦅', 2),
  collector: icon('Paleontologist field kit', '🧰', 3),
  sync: icon('Crimson fang fossil', '🔻', 1),
  rows: icon('Sapphire shell fossil', '🐚', 2),
  paw: icon('Amber track fossil', '👣', 3),
  rand: icon('Emerald leaf fossil', '🍃', 0),
  value: icon('Museum amber token', '🟡', 1),
} as const
