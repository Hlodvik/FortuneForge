import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import '../../../../../../fortuneforge.client/src/dev/standaloneGamePreview.css'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { LiarsDiceGame } from '../LiarsDiceGame'
import { HttpLiarsDiceGateway } from '../httpLiarsDiceGateway'
import '../liarsDice.css'
import '../liarsDiceViewport.css'
import './preview.css'

const root = document.getElementById('root')
if (!root) throw new Error("Liar's Dice preview root was not found.")
createRoot(root).render(<StrictMode><InGameShell title="Liar’s Dice" bodyClassName="standalone-game-preview__body"><LiarsDiceGame gateway={new HttpLiarsDiceGateway()} /></InGameShell></StrictMode>)
