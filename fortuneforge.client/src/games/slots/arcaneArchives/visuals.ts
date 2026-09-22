import amberOracleRune from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-amber-oracle-rune-v2.webp'
import cabinetEmblem from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-cabinet-emblem-v2.webp'
import candle from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-candle-v2.webp'
import celestialSpellburst from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-celestial-spellburst-v2.webp'
import crystal from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-crystal-v2.webp'
import emeraldFortuneRune from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-emerald-fortune-rune-v2.webp'
import enchantedBookSatchel from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-enchanted-book-satchel-v2.webp'
import grandGrimoire from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-grand-grimoire-v2.webp'
import library from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-library-v2.webp'
import livingLightningRune from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-living-lightning-rune-v2.webp'
import manaToken from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-mana-token-v2.webp'
import owl from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-owl-v2.webp'
import potion from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-potion-v2.webp'
import quill from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-quill-v2.webp'
import rubyEchoRune from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-ruby-echo-rune-v2.webp'
import sapphireMoonRune from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-sapphire-moon-rune-v2.webp'
import scroll from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-scroll-v2.webp'
import secretLibraryDoorway from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-secret-library-doorway-v2.webp'
import triadMagicWands from '../../../assets/slots/games/arcane-archives/optimized/arcane-archives-triad-magic-wands-v2.webp'

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
  wild: grandGrimoire,
  free: secretLibraryDoorway,
  power: celestialSpellburst,
  energy: livingLightningRune,
  lineBonus: triadMagicWands,
  collector: enchantedBookSatchel,
  sync: rubyEchoRune,
  rows: sapphireMoonRune,
  paw: amberOracleRune,
  rand: emeraldFortuneRune,
  value: manaToken,
} as const
