import amberOracleRune from './assets/art/optimized/arcane-archives-amber-oracle-rune-v2.webp'
import cabinetEmblem from './assets/art/optimized/arcane-archives-cabinet-emblem-v2.webp'
import candle from './assets/art/optimized/arcane-archives-candle-v2.webp'
import aetherConstellation from './assets/art/optimized/arcane-archives-aether-constellation-v1.webp'
import crystal from './assets/art/optimized/arcane-archives-crystal-v2.webp'
import fortuneConstellationRune from './assets/art/optimized/arcane-archives-fortune-constellation-rune-v7.webp'
import enchantedBookSatchel from './assets/art/optimized/arcane-archives-enchanted-book-satchel-v2.webp'
import grandGrimoire from './assets/art/optimized/arcane-archives-grand-grimoire-v2.webp'
import library from './assets/art/optimized/arcane-archives-library-v2.webp'
import livingLightningRune from './assets/art/optimized/arcane-archives-living-lightning-rune-v2.webp'
import manaToken from './assets/art/optimized/arcane-archives-mana-token-v2.webp'
import openBookConstellationRune from './assets/art/optimized/arcane-archives-open-book-constellation-rune-v5.webp'
import owl from './assets/art/optimized/arcane-archives-owl-v2.webp'
import potion from './assets/art/optimized/arcane-archives-potion-v3.webp'
import quill from './assets/art/optimized/arcane-archives-quill-v3.webp'
import rubyEchoRune from './assets/art/optimized/arcane-archives-ruby-echo-rune-v2.webp'
import sapphireMoonRune from './assets/art/optimized/arcane-archives-sapphire-moon-rune-v2.webp'
import scroll from './assets/art/optimized/arcane-archives-scroll-v2.webp'
import secretLibraryDoorway from './assets/art/optimized/arcane-archives-secret-library-doorway-v2.webp'
import traditionalWand from './assets/art/optimized/arcane-archives-traditional-wand-v6.webp'

export const ARCANE_ARCHIVES_VISUALS = {
  backdrop: library,
  emblem: cabinetEmblem,
  accent: crystal,
  candle,
  quill,
  scroll,
  potion,
  crystal,
  owl,
  bookRune: openBookConstellationRune,
  wild: grandGrimoire,
  free: secretLibraryDoorway,
  power: aetherConstellation,
  energy: livingLightningRune,
  lineBonus: traditionalWand,
  collector: enchantedBookSatchel,
  sync: rubyEchoRune,
  rows: sapphireMoonRune,
  paw: amberOracleRune,
  rand: fortuneConstellationRune,
  value: manaToken,
} as const
