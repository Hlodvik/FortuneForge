import { ART } from './art.js'
import { FRONTIER_RULES, FRONTIER_SYMBOLS, INITIAL_REELS, OUTLAW_RULES, PAYLINES, PAYTABLE } from './rules.js'

type Symbol = { id: string; label: string }
const definitions = Object.fromEntries(FRONTIER_SYMBOLS.map((symbol: Symbol) => [symbol.id, {
  id: symbol.id, label: symbol.label, image: ART.symbols[symbol.id as keyof typeof ART.symbols],
}]))

export const FRONTIER_FORTUNE_SLOT_GAME = {
  id: 'high-noon-fortune',
  routes: { play: '/slots/frontier-fortune', demo: null },
  catalog: {
    id: 'high-noon-fortune', title: 'Frontier Fortune', shortTitle: 'Frontier Fortune',
    description: 'Build your ante on the western reels, then pick and hold your way to Outlaw’s Hand.',
    image: ART.title, imagePresentation: 'contain' as const, slotDivBackgroundImage: ART.town,
  },
  experience: {
    id: 'frontier-fortune-experience-v2', shellBackdrop: 'theme', mascot: null,
    cabinet: {
      id: 'frontier-fortune-cabinet-v2', chrome: 'simple', title: 'Frontier Fortune',
      accessibleName: 'Frontier Fortune western slot machine', eyebrow: '', subtitle: '',
      backdropImage: ART.town, visualsBackdropImage: ART.town, pageBackdropImage: ART.town,
      titleArtwork: { src: ART.title, width: 720, height: 320 },
      palette: { shellTop: '#35271b', shellBottom: '#15100b', panel: '#14120e', trim: '#999184', trimBright: '#d6cdb9', accent: '#aa8650', glow: '#c4ad7e', text: '#f1e6ca' },
    },
    features: {}, sounds: { id: 'frontier-fortune-sounds-v1' },
    symbols: {
      id: 'frontier-fortune-symbols-v1', serverSymbolSetId: 'frontier-fortune-symbols-v1', definitions,
      animationSymbolIds: FRONTIER_SYMBOLS.map((symbol: Symbol) => symbol.id),
      guideEntries: FRONTIER_SYMBOLS.map((symbol: Symbol) => ({
        symbol: symbol.id,
        firstLabel: symbol.id === 'WANTED' ? '2 / 3+ anywhere' : '3 / 4 / 5',
        firstValue: symbol.id === 'WANTED' ? '+1 / +2 saved ante chips' : [3, 4, 5].map(count => `${PAYTABLE[symbol.id][count]}×`).join(' / '),
      })),
    },
    help: {
      paylineCount: PAYLINES.length, paylinePatternIds: PAYLINES.map((_: unknown, index: number) => index + 1),
      extraSections: [
        { badge: String(OUTLAW_RULES.ante.requiredChips), title: 'Build the ante', body: 'Two WANTED posters add one saved ante chip; three or more add two. A funded ante earns Outlaw’s Hand.' },
        { badge: String(OUTLAW_RULES.hand.deals), title: 'Outlaw’s Hand', body: 'Three included deals on the same reels. Choose one card from each open reel, then HOLD hand cards before later deals. The final hand awards once.' },
      ],
    },
    rules: {
      gameId: FRONTIER_RULES.gameId, pointValueInCents: FRONTIER_RULES.pointValueInCents,
      defaultWagerRand: FRONTIER_RULES.minWagerPoints * FRONTIER_RULES.pointValueInCents / 100,
      wagerOptions: Array.from({ length: (FRONTIER_RULES.maxWagerPoints - FRONTIER_RULES.minWagerPoints) / FRONTIER_RULES.stepPoints + 1 }, (_, index) => FRONTIER_RULES.minWagerPoints + index * FRONTIER_RULES.stepPoints),
      initialReels: INITIAL_REELS,
    },
  },
} as const
