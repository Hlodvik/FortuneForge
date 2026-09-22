import allSeeingOdin from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-all-seeing-odin-v2.webp'
import nordicCabinetEmblem from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-cabinet-emblem-v2.webp'
import amberHammerRune from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-amber-hammer-rune-v2.webp'
import auroraLightning from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-aurora-lightning-v2.webp'
import battleAxe from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-battle-axe-v2.webp'
import bifrostBridge from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-bifrost-bridge-v2.webp'
import carvedRunestone from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-carved-runestone-v2.webp'
import dragonProwLongship from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-dragon-prow-longship-v2.webp'
import fjord from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-fjord-v2.webp'
import crimsonWolfRune from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-crimson-wolf-rune-v2.webp'
import emeraldWorldTreeRune from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-emerald-world-tree-rune-v2.webp'
import feastHallDrinkingHorn from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-feast-hall-drinking-horn-v2.webp'
import mjolnirThunder from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-mjolnir-thunder-v2.webp'
import sapphireWaveRune from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-sapphire-wave-rune-v2.webp'
import silverRune from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-silver-rune-v2.webp'
import tripleBattleAxes from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-triple-battle-axes-v2.webp'
import valkyrieTreasureChest from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-valkyrie-treasure-chest-v2.webp'
import vikingRoundShield from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-viking-round-shield-v2.webp'
import wingedValkyrie from '../../../assets/slots/games/nordic-legends/optimized/nordic-legends-winged-valkyrie-v2.webp'

export const NORDIC_LEGENDS_SYMBOL_IMAGES = {
  '2': carvedRunestone, '3': feastHallDrinkingHorn, '4': vikingRoundShield, '5': battleAxe, '6': dragonProwLongship, '7': wingedValkyrie,
  ACE: allSeeingOdin, FREE: bifrostBridge, POWER: mjolnirThunder, BOLT: auroraLightning,
  BANANA: tripleBattleAxes, PAW: valkyrieTreasureChest,
  SEAL_SYNC: crimsonWolfRune, SEAL_ROWS: sapphireWaveRune, SEAL_PAW: amberHammerRune, SEAL_RAND: emeraldWorldTreeRune,
  VALUE: silverRune,
  CABINET_EMBLEM: nordicCabinetEmblem, BACKDROP: fjord,
} as const
