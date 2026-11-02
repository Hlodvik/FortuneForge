import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import '../../../../../../fortuneforge.client/src/dev/standaloneGamePreview.css'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { CasinoWarGame } from '../CasinoWarGame'
import './preview.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <InGameShell title="Casino War" bodyClassName="standalone-game-preview__body"><CasinoWarGame /></InGameShell>
  </StrictMode>,
)
