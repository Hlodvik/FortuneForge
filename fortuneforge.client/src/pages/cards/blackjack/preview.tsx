import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BlackjackTablePreview } from './BlackjackTablePage'

const root = document.getElementById('root')
if (root === null) throw new Error('Blackjack preview root is missing.')
const mode = new URLSearchParams(window.location.search).get('mode') === 'betting' ? 'betting' : 'active'
createRoot(root).render(<StrictMode><BlackjackTablePreview mode={mode} /></StrictMode>)
