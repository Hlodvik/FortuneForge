import { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { HiLoGame } from '../HiLoGame'
import { hiLoRanks, type HiLoCard } from '../contracts'
import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import type { AccountSummary } from '../../../../../../fortuneforge.client/src/features/account/services/accountsApi'
import { PlayingCard } from '../../../../../../fortuneforge.client/src/games/cards/shared/PlayingCard'
import type { CardRank } from '../../../../../../fortuneforge.client/src/games/cards/shared/cards'
import '../../../../../../fortuneforge.client/src/games/cards/shared/playingCards.css'
import './preview.css'

const renderCardFace = (card: HiLoCard) => <PlayingCard card={{ id: `hilo-${card.rank}-${card.suit}`, rank: (hiLoRanks.indexOf(card.rank) + 1) as CardRank, suit: card.suit }}/>
function Preview() {
  const [balance, setBalance] = useState(10000)
  const account: AccountSummary = {
    userId: 'standalone-practice', playerName: 'Practice player', email: '', createdAtUtc: '2026-10-23T00:00:00Z', role: 'player',
    balances: { slotsCredits: balance, freeGames: 0 },
    slots: { spinsPlayed: 0, wins: 0, losses: 0, creditsWagered: 0, creditsWon: 0, netCredits: 0 },
  }
  return <InGameShell account={account} title="Hi-Lo" theme="casino" className="hilo-preview" actions={<span className="hilo-preview-mode">Practice play</span>}>
    <HiLoGame playerId="standalone-practice" onBalanceChange={setBalance} renderCardFace={renderCardFace}/>
  </InGameShell>
}
createRoot(document.getElementById('root')!).render(<Preview/>)
