import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BaccaratGame } from '../BaccaratGame'
import type { BaccaratCard, BaccaratGateway, BaccaratRound } from '../contracts'
import './preview.css'

let previewRound = 0
let previewBalance = 1000
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

if (new URLSearchParams(window.location.search).has('mock')) {
  // Fill the preview roadmap so its density can be checked without wagering
  // through dozens of demonstration hands. Production history is untouched.
  const key = 'fortuneforge:baccarat:history:guest'
  try {
    const existing = JSON.parse(localStorage.getItem(key) ?? '[]') as unknown
    if (!Array.isArray(existing) || existing.length < 10) {
      const pattern = ['banker', 'banker', 'player', 'banker', 'tie', 'player', 'player', 'banker', 'player', 'tie', 'banker', 'player'] as const
      localStorage.setItem(key, JSON.stringify(Array.from({ length: 58 }, (_, index) => ({
        roundId: `preview-history-${index}`,
        outcome: pattern[index % pattern.length],
        cardsUsed: index % 4 === 0 ? 5 : 4,
        natural: index % 6 === 0,
      }))))
    }
  } catch { /* preview still works when storage is unavailable */ }
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BaccaratGame gateway={new URLSearchParams(window.location.search).has('mock') ? previewGateway : undefined} />
  </StrictMode>,
)
