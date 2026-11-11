import type { ReactNode } from 'react'
import { HoldemActionBar } from './HoldemActionBar'
import { HoldemLobby } from './HoldemLobby'
import { HoldemQueue } from './HoldemQueue'
import { HoldemTableSurface } from './HoldemTableSurface'
import './holdem.css'

const views = { HoldemActionBar, HoldemLobby, HoldemQueue, HoldemTableSurface }

export type TexasHoldemBrowserViews = typeof views

export interface TexasHoldemBrowserProps {
  renderHoldem: (views: TexasHoldemBrowserViews) => ReactNode
}

export default function TexasHoldemBrowser({ renderHoldem }: TexasHoldemBrowserProps) {
  return renderHoldem(views)
}
