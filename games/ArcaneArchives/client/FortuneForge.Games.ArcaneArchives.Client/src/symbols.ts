// Edited: 2026-11-18T14:43:56-06:00 | Computer: Vinnabok
// Edited: 2026-11-18T16:27:28-06:00 | Computer: Vinnabok
import type { SlotSymbolSet } from '@fortuneforge/slot-client-core'
import { createClassicPaylineGuide } from '@fortuneforge/slot-game-runtime/engine-guide'
import { ARCANE_ARCHIVES_VISUALS as art } from './visuals'

export const ARCANE_ARCHIVES_GAME_ID = 'arcane-archives-v1'

const paylineSymbols = {
  '2': { label: 'Ogham Right', image: art.oghamRight },
  '3': { label: 'Ogham Left', image: art.oghamLeft },
  '4': { label: 'Ogham Three Slashes', image: art.oghamSlashes },
  '5': { label: 'VII', image: art.romanSeven },
  '6': { label: 'Pentagram', image: art.pentagram },
}

const reelSymbols = {
  ...paylineSymbols,
  WAND: { label: 'Enchanted Wand', image: art.wand },
  POWER: { label: 'Aether Constellation', image: art.aetherConstellation },
  FORTUNE_RUNE: { label: 'Fortune Constellation', image: art.fortuneConstellation },
  BOLT: { label: 'Lightning Constellation', image: art.lightningConstellation },
  ACE: { label: 'Grand Grimoire', image: art.grandGrimoire },
  MANDRAKE_ROOT: { label: 'Mandrake Root', image: art.mandrakeRoot },
  NIGHTSHADE_BERRIES: { label: 'Nightshade Berries', image: art.nightshadeBerries },
  MOONCAP_MUSHROOM: { label: 'Mooncap Mushroom', image: art.mooncapMushroom },
  PHOENIX_FEATHER: { label: 'Phoenix Feather', image: art.phoenixFeather },
  WOLFSBANE_WINE: { label: 'Wolfsbane Wine', image: art.wolfsbaneWine },
  QUICKSILVER: { label: 'Quicksilver', image: art.quicksilver },
}

export const ARCANE_ARCHIVES_SYMBOLS: SlotSymbolSet = {
  id: 'arcane-archives-symbols-v2',
  serverSymbolSetId: 'arcane-archives-payline-symbols-v2',
  definitions: Object.fromEntries(Object.entries(reelSymbols).map(([id, symbol]) => [
    id,
    { ...symbol, id, animatedImage: symbol.image },
  ])),
  guideEntries: createClassicPaylineGuide(ARCANE_ARCHIVES_GAME_ID, Object.keys(paylineSymbols)),
}
