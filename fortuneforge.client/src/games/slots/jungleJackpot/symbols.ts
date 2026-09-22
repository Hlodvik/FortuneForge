import amberSunRelic from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-amber-sun-relic-v2.webp'
import blueOrchidRelic from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-blue-orchid-relic-v2.webp'
import emeraldFrogRelic from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-emerald-frog-relic-v2.webp'
import explorerFieldPack from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-explorer-field-pack-v2.webp'
import fireflyCharge from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-firefly-charge-v2.webp'
import goldenJungleTemple from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-golden-jungle-temple-v2.webp'
import jungleCabinetEmblem from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-cabinet-emblem-v2.webp'
import goldenTiger from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-golden-tiger-v2.webp'
import hiddenWaterfall from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-hidden-waterfall-v2.webp'
import jaguarFangRelic from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-jaguar-fang-relic-v2.webp'
import playfulMonkey from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-playful-monkey-v2.webp'
import rainforestJaguar from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-rainforest-jaguar-v2.webp'
import radiantSunIdol from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-radiant-sun-idol-v2.webp'
import scarletParrot from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-scarlet-parrot-v2.webp'
import treeFrog from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-tree-frog-v2.webp'
import tripleJungleVines from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-triple-jungle-vines-v2.webp'
import tropicalLeaf from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-tropical-leaf-v2.webp'
import templeCoin from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-temple-coin-v2.webp'
import templeClearing from '../../../assets/slots/games/jungle-jackpot/optimized/jungle-jackpot-temple-clearing-v2.webp'

export const JUNGLE_JACKPOT_SYMBOL_IMAGES = {
  '2': tropicalLeaf, '3': scarletParrot, '4': treeFrog, '5': playfulMonkey, '6': rainforestJaguar, '7': goldenJungleTemple,
  ACE: goldenTiger, FREE: hiddenWaterfall, POWER: radiantSunIdol, BOLT: fireflyCharge,
  BANANA: tripleJungleVines, PAW: explorerFieldPack,
  SEAL_SYNC: jaguarFangRelic, SEAL_ROWS: blueOrchidRelic, SEAL_PAW: amberSunRelic, SEAL_RAND: emeraldFrogRelic,
  VALUE: templeCoin,
  CABINET_EMBLEM: jungleCabinetEmblem, BACKDROP: templeClearing,
} as const
