import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import { App } from './App'
import './preview.css'

const root = document.getElementById('root')
if (!root) throw new Error('The Craps preview root was not found.')

createRoot(root).render(
  <StrictMode>
    <InGameShell title="Craps" theme="casino" className="craps-page" bodyClassName="craps-route__body">
      <App />
    </InGameShell>
  </StrictMode>,
)
