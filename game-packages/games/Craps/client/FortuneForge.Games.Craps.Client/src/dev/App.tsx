import { useMemo } from 'react'
import { CrapsGame } from '../CrapsGame'
import { HttpCrapsGateway } from '../httpCrapsGateway'
import tableArtworkUrl from '../assets/craps-table-backdrop.png'

export function App() {
  const gateway = useMemo(() => new HttpCrapsGateway(), [])
  const isAnotherPlayersTurn = new URLSearchParams(window.location.search).get('turn') === 'other'
  return (
    <CrapsGame
      gateway={gateway}
      playerName="Local Player"
      tableLabel="Practice table"
      tableArtworkUrl={tableArtworkUrl}
      isYourTurn={!isAnotherPlayersTurn}
      activePlayerName="Maya"
    />
  )
}
