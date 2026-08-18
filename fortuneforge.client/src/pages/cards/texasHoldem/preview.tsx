import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { CreditTexasHoldemPreview } from './CreditTexasHoldemPage'
import { TexasHoldemPage } from './TexasHoldemPage'

const creditTable = new URLSearchParams(window.location.search).get('mode') === 'credit'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {creditTable
      ? <CreditTexasHoldemPreview />
      : <TexasHoldemPage playerName="Preview Player" returnHref="/demo/cards" />}
  </StrictMode>,
)
