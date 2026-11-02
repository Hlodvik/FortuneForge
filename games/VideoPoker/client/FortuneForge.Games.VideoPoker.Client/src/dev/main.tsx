import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import '../../../../../../fortuneforge.client/src/dev/standaloneGamePreview.css'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { VideoPokerGame } from '../VideoPokerGame'
import './preview.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <InGameShell title="Video Poker" bodyClassName="standalone-game-preview__body"><VideoPokerGame /></InGameShell>
  </StrictMode>,
)
