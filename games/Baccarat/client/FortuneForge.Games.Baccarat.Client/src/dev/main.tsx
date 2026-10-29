import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BaccaratGame } from '../BaccaratGame'
import type { BaccaratCard, BaccaratGateway, BaccaratRound } from '../contracts'
import { PlayingCard } from '../../../../../../fortuneforge.client/src/games/cards/shared/PlayingCard'
import type { CardRank } from '../../../../../../fortuneforge.client/src/games/cards/shared/cards'
import '../../../../../../fortuneforge.client/src/games/cards/shared/playingCards.css'
import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import '../../../../../../fortuneforge.client/src/app/routes/BaccaratRoute.css'
import './preview.css'

let previewRound = 0
let previewBalance = 1000
const faceRanks: Readonly<Record<BaccaratCard['rank'], CardRank>> = {
  ace: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7,
  eight: 8, nine: 9, ten: 10, jack: 11, queen: 12, king: 13,
}
const renderCardFace = (card: BaccaratCard) => <PlayingCard card={{
  id: `baccarat-preview-${card.rank}-${card.suit}`, rank: faceRanks[card.rank], suit: card.suit,
}} />
const previewHands = [
  { playerCards: [{ rank: 'nine', suit: 'hearts' }, { rank: 'king', suit: 'clubs' }], bankerCards: [{ rank: 'seven', suit: 'diamonds' }, { rank: 'queen', suit: 'spades' }] },
  { playerCards: [{ rank: 'four', suit: 'clubs' }, { rank: 'four', suit: 'hearts' }], bankerCards: [{ rank: 'nine', suit: 'clubs' }, { rank: 'king', suit: 'diamonds' }] },
  { playerCards: [{ rank: 'eight', suit: 'spades' }, { rank: 'king', suit: 'clubs' }], bankerCards: [{ rank: 'four', suit: 'diamonds' }, { rank: 'four', suit: 'spades' }] },
  { playerCards: [{ rank: 'four', suit: 'hearts' }, { rank: 'four', suit: 'clubs' }], bankerCards: [{ rank: 'three', suit: 'diamonds' }, { rank: 'three', suit: 'spades' }] },
  { playerCards: [{ rank: 'seven', suit: 'diamonds' }, { rank: 'queen', suit: 'spades' }], bankerCards: [{ rank: 'nine', suit: 'clubs' }, { rank: 'king', suit: 'diamonds' }] },
  { playerCards: [{ rank: 'seven', suit: 'diamonds' }, { rank: 'queen', suit: 'spades' }], bankerCards: [{ rank: 'seven', suit: 'hearts' }, { rank: 'king', suit: 'clubs' }] },
] as const satisfies readonly { playerCards: readonly BaccaratCard[]; bankerCards: readonly BaccaratCard[] }[]

function total(cards: readonly BaccaratCard[]) {
  const points: Partial<Record<BaccaratCard['rank'], number>> = { ace: 1, two: 2, three: 3, four: 4, five: 5, six: 6, seven: 7, eight: 8, nine: 9 }
  return cards.reduce((sum, card) => sum + (points[card.rank] ?? 0), 0) % 10
}

function money(value: number) { return Math.round(value * 100) / 100 }

const previewGateway: BaccaratGateway = {
  async getStatus() { return { available: true, minimumStake: .25, maximumStake: 100, stakeIncrement: .25, balance: previewBalance, mode: 'preview', supportsPairBets: true } },
  async createRound(betSide, stake, options): Promise<BaccaratRound> {
    previewRound += 1
    const { playerCards, bankerCards } = previewHands[(previewRound - 1) % previewHands.length]
    const playerTotal = total(playerCards)
    const bankerTotal = total(bankerCards)
    const outcome = playerTotal > bankerTotal ? 'player' : bankerTotal > playerTotal ? 'banker' : 'tie'
    const disposition = outcome === betSide ? 'win' : outcome === 'tie' ? 'push' : 'loss'
    const profit = money(disposition === 'win' ? stake * (betSide === 'tie' ? 8 : betSide === 'banker' ? .95 : 1) : disposition === 'push' ? 0 : -stake)
    const playerPairStake = options?.playerPairStake ?? 0
    const bankerPairStake = options?.bankerPairStake ?? 0
    const playerPairDisposition = playerPairStake === 0 ? null : playerCards[0].rank === playerCards[1].rank ? 'win' : 'loss'
    const bankerPairDisposition = bankerPairStake === 0 ? null : bankerCards[0].rank === bankerCards[1].rank ? 'win' : 'loss'
    const playerPairProfit = playerPairDisposition === null ? 0 : money(playerPairDisposition === 'win' ? playerPairStake * 11 : -playerPairStake)
    const bankerPairProfit = bankerPairDisposition === null ? 0 : money(bankerPairDisposition === 'win' ? bankerPairStake * 11 : -bankerPairStake)
    const totalStake = money(stake + playerPairStake + bankerPairStake)
    const combinedProfit = money(profit + playerPairProfit + bankerPairProfit)
    const combinedTotalReturn = money(totalStake + combinedProfit)
    previewBalance = money(previewBalance + combinedProfit)
    return {
      roundId: `preview-${previewRound}`, balance: previewBalance, betSide, stake, phase: 'settled',
      playerCards, bankerCards, playerTotal, bankerTotal,
      outcome, endedOnNatural: playerTotal >= 8 || bankerTotal >= 8,
      disposition, profit, totalReturn: money(stake + profit),
      playerPairStake, bankerPairStake, playerPairDisposition, bankerPairDisposition,
      playerPairProfit, bankerPairProfit,
      playerPairTotalReturn: money(playerPairStake + playerPairProfit),
      bankerPairTotalReturn: money(bankerPairStake + bankerPairProfit),
      totalStake, combinedProfit, combinedTotalReturn,
    }
  },
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <InGameShell theme="casino" title="Baccarat" className="baccarat-page" bodyClassName="baccarat-route__body">
      <BaccaratGame gateway={new URLSearchParams(window.location.search).has('mock') ? previewGateway : undefined} renderCardFace={renderCardFace} showTitle={false} />
    </InGameShell>
  </StrictMode>,
)
