import { useMemo } from 'react'
import { CrapsGame } from '../CrapsGame'
import { HttpCrapsGateway } from '../httpCrapsGateway'

export function App() {
  const gateway = useMemo(() => new HttpCrapsGateway(), [])
  const isAnotherPlayersTurn = new URLSearchParams(window.location.search).get('turn') === 'other'
  return (
    <CrapsGame
      gateway={gateway}
      isYourTurn={!isAnotherPlayersTurn}
      activePlayerName="Maya"
    />
  )
}
