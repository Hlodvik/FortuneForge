import cherrySymbol from '../../assets/slots/symbols/cherry.gif'
import wukongMedallion from '../../assets/slots/symbols/wukong/wukong-medallion.png'

export type SlotGameCatalogEntry = {
  id: string
  title: string
  shortTitle: string
  description: string
  image: string
  imagePresentation: 'contain' | 'cover'
  slotDivBackgroundImage?: string
  playHref: string | null
  serverGameIds: readonly string[]
}

// The game picker, recent-play history, and direct routes all share this
// catalog so titles, thumbnails, and availability cannot drift apart.
export const WUKONG_JOURNEY_TO_THE_WEST: SlotGameCatalogEntry = {
  id: 'wukong-journey-to-the-west',
  title: "Wukong's Journey to the West",
  shortTitle: "Wukong's Journey",
  description: 'Ride the nimbus clouds through five celestial reels with Wukong at your side.',
  image: wukongMedallion,
  imagePresentation: 'contain',
  playHref: '/slots/wukong',
  serverGameIds: ['classic-demo-v1'],
}

export const SLOT_GAME_CATALOG: readonly SlotGameCatalogEntry[] = [
  WUKONG_JOURNEY_TO_THE_WEST,
  {
    id: 'rainbow-realm',
    title: 'Rainbow Realm',
    shortTitle: 'Rainbow Realm',
    description: 'A bright return to the classic fruit symbols, led by the lucky cherry.',
    image: cherrySymbol,
    imagePresentation: 'contain',
    playHref: '/slots/rainbow-realm',
    serverGameIds: ['rainbow-realm-fruits-v1'],
  },
]

export function findSlotGameByServerId(gameId: string): SlotGameCatalogEntry | null {
  return SLOT_GAME_CATALOG.find((game) => game.serverGameIds.includes(gameId)) ?? null
}
