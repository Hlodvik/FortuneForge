import amberSunGem from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-amber-sun-gem-v2.webp'
import dragonCabinetEmblem from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-cabinet-emblem-v2.webp'
import ancientDragon from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-ancient-dragon-v2.webp'
import chargingKnight from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-charging-knight-v2.webp'
import crimsonFireGem from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-crimson-fire-gem-v2.webp'
import dragonCoin from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-dragon-coin-v2.webp'
import dragonfireCharge from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-dragonfire-charge-v2.webp'
import emberLair from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-ember-lair-v2.webp'
import emberVault from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-ember-vault-v2.webp'
import emeraldEarthGem from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-emerald-earth-gem-v2.webp'
import enchantedTreasureChest from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-enchanted-treasure-chest-v2.webp'
import jeweledGoblet from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-jeweled-goblet-v2.webp'
import knightSword from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-knight-sword-v2.webp'
import mountainKingCastle from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-mountain-king-castle-v2.webp'
import royalGoldCoin from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-royal-gold-coin-v2.webp'
import royalRuby from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-royal-ruby-v2.webp'
import royalTowerShield from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-royal-tower-shield-v2.webp'
import sapphireFrostGem from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-sapphire-frost-gem-v2.webp'
import tripleDragonClaws from '../../../assets/slots/games/dragon-hoard/optimized/dragon-hoard-triple-dragon-claws-v2.webp'

export const DRAGON_HOARD_SYMBOL_IMAGES = {
  '2': royalGoldCoin, '3': jeweledGoblet, '4': knightSword, '5': royalTowerShield, '6': chargingKnight, '7': mountainKingCastle,
  ACE: ancientDragon, FREE: emberLair, POWER: royalRuby, BOLT: dragonfireCharge,
  BANANA: tripleDragonClaws, PAW: enchantedTreasureChest,
  SEAL_SYNC: crimsonFireGem, SEAL_ROWS: sapphireFrostGem, SEAL_PAW: amberSunGem, SEAL_RAND: emeraldEarthGem,
  VALUE: dragonCoin,
  CABINET_EMBLEM: dragonCabinetEmblem, BACKDROP: emberVault,
} as const
