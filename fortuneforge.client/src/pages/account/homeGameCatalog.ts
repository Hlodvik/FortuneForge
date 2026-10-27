import { SLOT_GAME_CATALOG } from '../../games/slots/catalog'

export type GameCategory = 'Slots' | 'Card games' | 'Table games' | 'Other games'
export type LobbyGame = {
  id: string
  href: string
  title: string
  category: GameCategory
  image?: string
  imagePresentation?: 'contain' | 'cover'
}

const nonSlotGames: readonly Omit<LobbyGame, 'id'>[] = [
  { href: '/cards/blackjack', title: 'Fortune Blackjack', category: 'Card games' },
  { href: '/cards/texas-holdem', title: 'Texas Hold’em', category: 'Card games' },
  { href: '/cards/solitaire', title: 'Competitive Solitaire', category: 'Card games' },
  { href: '/cards/three-card-poker', title: 'Three Card Poker', category: 'Card games' },
  { href: '/cards/house-holdem', title: 'House Hold’em', category: 'Card games' },
  { href: '/cards/hearts', title: 'Hearts', category: 'Card games' },
  { href: '/games/hi-lo', title: 'Hi-Lo', category: 'Card games' },
  { href: '/games/casino-war', title: 'Casino War', category: 'Card games' },
  { href: '/games/video-poker', title: 'Video Poker', category: 'Card games' },
  { href: '/games/dragon-tiger', title: 'Dragon Tiger', category: 'Table games' },
  { href: '/games/andar-bahar', title: 'Andar Bahar', category: 'Table games' },
  { href: '/games/baccarat', title: 'Baccarat', category: 'Table games' },
  { href: '/games/roulette', title: 'Roulette', category: 'Table games' },
  { href: '/games/craps', title: 'Craps', category: 'Table games' },
  { href: '/games/sic-bo', title: 'Sic Bo', category: 'Table games' },
  { href: '/games/liars-dice', title: 'Liar’s Dice', category: 'Table games' },
  { href: '/games/mines', title: 'Mines', category: 'Other games' },
  { href: '/games/keno', title: 'Keno', category: 'Other games' },
  { href: '/games/2048', title: '2048', category: 'Other games' },
  { href: '/games/drop-merge', title: 'Drop Merge', category: 'Other games' },
]

export const HOME_GAME_CATALOG: readonly LobbyGame[] = [
  ...nonSlotGames.map(game => ({ ...game, id: game.href.split('/').at(-1)! })),
  ...SLOT_GAME_CATALOG.flatMap(game => game.playHref ? [{
    id: game.id, href: game.playHref, title: game.title, category: 'Slots' as const,
    image: game.image, imagePresentation: game.imagePresentation,
  }] : []),
]

export const HOME_CATEGORIES = ['Popular', 'Slots', 'Card games', 'Table games', 'Other games'] as const
export type CategoryFilter = typeof HOME_CATEGORIES[number]

export function parseHomeCategory(category: string | null): CategoryFilter {
  const legacyCategories: Record<string, CategoryFilter> = { 'All games': 'Popular', Cards: 'Card games', Casino: 'Table games', Puzzles: 'Other games' }
  return HOME_CATEGORIES.find(item => item === category) ?? legacyCategories[category ?? ''] ?? 'Popular'
}
