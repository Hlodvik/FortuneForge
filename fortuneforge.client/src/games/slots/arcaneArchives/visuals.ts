import { createSlotBackdropSvg, createSlotIconSvg } from '../shared/themedSvg'

const palette = [
  ['#8b5cf6', '#251044', '#f7d36a', '#c084fc'],
  ['#1fc8b2', '#092f3c', '#f4c95d', '#58f1d3'],
  ['#ee6f9d', '#4d183c', '#ffd36b', '#ff8ec1'],
  ['#4d8dff', '#15275f', '#f5c45d', '#71c7ff'],
] as const

function icon(label: string, glyph: string, variant: number): string {
  const [background, backgroundDeep, rim, glow] = palette[variant % palette.length]
  return createSlotIconSvg({ label, glyph, background, backgroundDeep, rim, glow })
}

export const ARCANE_ARCHIVES_VISUALS = {
  backdrop: createSlotBackdropSvg({
    label: 'Moonlit Arcane Archives library',
    motif: '📚',
    skyTop: '#150b38',
    skyBottom: '#43205f',
    horizon: '#301c48',
    accent: '#c084fc',
    ground: '#100a27',
  }),
  emblem: icon('Arcane Archives emblem', '📚', 0),
  accent: icon('Arcane crystal accent', '🔮', 1),
  candle: icon('Whispering candle', '🕯️', 0),
  quill: icon('Moon-feather quill', '🪶', 1),
  scroll: icon('Sealed spell scroll', '📜', 2),
  potion: icon('Prismatic potion', '🧪', 3),
  crystal: icon('Oracle crystal ball', '🔮', 0),
  owl: icon('Archivist owl', '🦉', 1),
  wild: icon('Grand grimoire wild', '📖', 2),
  free: icon('Secret library doorway', '🚪', 3),
  power: icon('Celestial spellburst', '✨', 0),
  energy: icon('Living lightning rune', '⚡', 1),
  lineBonus: icon('Triad of magic wands', '🪄', 2),
  collector: icon('Enchanted book satchel', '🎒', 3),
  sync: icon('Ruby echo rune', '🔺', 2),
  rows: icon('Sapphire moon rune', '🌙', 3),
  paw: icon('Amber oracle rune', '👁️', 0),
  rand: icon('Emerald fortune rune', '✴️', 1),
  value: icon('Raw mana shard', '💠', 3),
} as const
