import { createSlotBackdropSvg, createSlotIconSvg } from '../shared/themedSvg'

const palette = [
  ['#2746b8', '#070d3c', '#f5d96c', '#54c9ff'],
  ['#8b2dc0', '#2b0a48', '#ffe271', '#ee6cff'],
  ['#0a9d8c', '#062f3e', '#f7ca5f', '#5effdf'],
  ['#e95745', '#511329', '#ffd773', '#ff8b6d'],
] as const

function icon(label: string, glyph: string, variant: number): string {
  const [background, backgroundDeep, rim, glow] = palette[variant % palette.length]
  return createSlotIconSvg({ label, glyph, background, backgroundDeep, rim, glow })
}

export const COSMIC_FORTUNE_VISUALS = {
  backdrop: createSlotBackdropSvg({
    label: 'Cosmic Fortune deep-space launch corridor',
    motif: '🪐',
    skyTop: '#05092b',
    skyBottom: '#35125c',
    horizon: '#162a61',
    accent: '#54c9ff',
    ground: '#050819',
  }),
  emblem: icon('Cosmic Fortune emblem', '🚀', 0),
  accent: icon('Orbiting star accent', '🌟', 1),
  satellite: icon('Fortune satellite', '🛰️', 0),
  comet: icon('Silver comet', '☄️', 1),
  moon: icon('Crescent moon station', '🌙', 2),
  planet: icon('Ringed gas giant', '🪐', 3),
  astronaut: icon('Lucky astronaut', '🧑‍🚀', 0),
  rocket: icon('Interstellar rocket', '🚀', 1),
  wild: icon('Alien captain wild', '👽', 2),
  free: icon('Wormhole free game', '🌀', 3),
  power: icon('Supernova power core', '🌟', 0),
  energy: icon('Atomic plasma charge', '⚛️', 1),
  lineBonus: icon('Meteor shower bonus', '🌠', 2),
  collector: icon('Tractor-beam saucer', '🛸', 3),
  sync: icon('Crimson binary star', '🔴', 3),
  rows: icon('Sapphire ice planet', '🔵', 0),
  paw: icon('Amber solar world', '🟠', 1),
  rand: icon('Emerald garden planet', '🟢', 2),
  value: icon('Dark-matter crystal', '💎', 0),
} as const
