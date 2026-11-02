import { InGameShell } from '../../../../../fortuneforge.client/src/components/InGameShell'
import '../../../../../fortuneforge.client/src/dev/standaloneGamePreview.css'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { SicBoGame } from '@fortuneforge/games-sic-bo'
import '@fortuneforge/games-sic-bo/styles.css'
import './preview.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <InGameShell title="Sic Bo" bodyClassName="standalone-game-preview__body"><SicBoGame /></InGameShell>
  </StrictMode>,
)
