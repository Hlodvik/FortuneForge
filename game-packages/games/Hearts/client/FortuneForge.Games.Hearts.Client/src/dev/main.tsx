import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { HeartsGame } from '../HeartsGame'
import { HttpHeartsGateway } from '../httpHeartsGateway'
import '../hearts.css'

const root = document.getElementById('root')
if (!root) throw new Error('Hearts preview root was not found.')
createRoot(root).render(<StrictMode><HeartsGame gateway={new HttpHeartsGateway()} /></StrictMode>)
