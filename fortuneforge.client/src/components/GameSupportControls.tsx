import type { ReactNode } from 'react'
import type { AccountSummary } from '../features/account/services/accountsApi'
import { GameReportControl } from './GameReportControl'
import './GameSupportControls.css'

type GameSupportControlsProps = Readonly<{
  account?: AccountSummary
  gameTitle?: string
  children?: ReactNode
}>

export function GameSupportControls({ account, gameTitle, children }: GameSupportControlsProps) {
  if (!account && !children) return null

  return <div className="game-support-controls" role="group" aria-label="Game help and reporting">
    {children}
    {account && <GameReportControl key={account.userId} gameTitle={gameTitle} />}
  </div>
}
