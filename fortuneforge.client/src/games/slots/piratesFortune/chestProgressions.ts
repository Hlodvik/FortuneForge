import emeraldChest from '../../../assets/slots/games/pirates-fortune/optimized/chests/emerald/empty.png'
import lapisChest from '../../../assets/slots/games/pirates-fortune/optimized/chests/lapis/empty.png'
import rubyChest from '../../../assets/slots/games/pirates-fortune/optimized/chests/ruby/empty.png'
import topazChest from '../../../assets/slots/games/pirates-fortune/optimized/chests/topaz/empty.png'

export const PIRATES_FORTUNE_CHEST_PROGRESSIONS = {
  ruby: {
    empty: rubyChest,
  },
  lapis: {
    empty: lapisChest,
  },
  topaz: {
    empty: topazChest,
  },
  emerald: {
    empty: emeraldChest,
  },
} as const
