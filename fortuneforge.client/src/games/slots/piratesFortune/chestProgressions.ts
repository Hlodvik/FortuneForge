import emeraldChest from '../../../assets/slots/games/pirates-fortune/optimized/chests/emerald/empty.png'
import emeraldLevel1 from '../../../assets/slots/games/pirates-fortune/optimized/chests/emerald/level-1.png'
import emeraldLevel2 from '../../../assets/slots/games/pirates-fortune/optimized/chests/emerald/level-2.png'
import emeraldLevel3 from '../../../assets/slots/games/pirates-fortune/optimized/chests/emerald/level-3.png'
import emeraldLevel4 from '../../../assets/slots/games/pirates-fortune/optimized/chests/emerald/level-4.png'
import lapisChest from '../../../assets/slots/games/pirates-fortune/optimized/chests/lapis/empty.png'
import lapisLevel1 from '../../../assets/slots/games/pirates-fortune/optimized/chests/lapis/level-1.png'
import lapisLevel2 from '../../../assets/slots/games/pirates-fortune/optimized/chests/lapis/level-2.png'
import lapisLevel3 from '../../../assets/slots/games/pirates-fortune/optimized/chests/lapis/level-3.png'
import lapisLevel4 from '../../../assets/slots/games/pirates-fortune/optimized/chests/lapis/level-4.png'
import rubyChest from '../../../assets/slots/games/pirates-fortune/optimized/chests/ruby/empty.png'
import rubyLevel1 from '../../../assets/slots/games/pirates-fortune/optimized/chests/ruby/level-1.png'
import rubyLevel2 from '../../../assets/slots/games/pirates-fortune/optimized/chests/ruby/level-2.png'
import rubyLevel3 from '../../../assets/slots/games/pirates-fortune/optimized/chests/ruby/level-3.png'
import rubyLevel4 from '../../../assets/slots/games/pirates-fortune/optimized/chests/ruby/level-4.png'
import topazChest from '../../../assets/slots/games/pirates-fortune/optimized/chests/topaz/empty.png'
import topazLevel1 from '../../../assets/slots/games/pirates-fortune/optimized/chests/topaz/level-1.png'
import topazLevel2 from '../../../assets/slots/games/pirates-fortune/optimized/chests/topaz/level-2.png'
import topazLevel3 from '../../../assets/slots/games/pirates-fortune/optimized/chests/topaz/level-3.png'
import topazLevel4 from '../../../assets/slots/games/pirates-fortune/optimized/chests/topaz/level-4.png'

export const PIRATES_FORTUNE_CHEST_PROGRESSIONS = {
  ruby: {
    empty: rubyChest,
    fillLevels: [rubyLevel1, rubyLevel2, rubyLevel3, rubyLevel4],
  },
  lapis: {
    empty: lapisChest,
    fillLevels: [lapisLevel1, lapisLevel2, lapisLevel3, lapisLevel4],
  },
  topaz: {
    empty: topazChest,
    fillLevels: [topazLevel1, topazLevel2, topazLevel3, topazLevel4],
  },
  emerald: {
    empty: emeraldChest,
    fillLevels: [emeraldLevel1, emeraldLevel2, emeraldLevel3, emeraldLevel4],
  },
} as const
