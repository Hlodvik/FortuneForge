import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import '../../../../../../fortuneforge.client/src/dev/standaloneGamePreview.css'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { AsteroidsGame } from '../AsteroidsGame'
import { HttpAsteroidsGateway } from '../httpAsteroidsGateway'
import '../asteroids.css'

const root = document.getElementById('root')
if (!root) throw new Error('Asteroids preview root was not found.')
createRoot(root).render(<StrictMode><InGameShell title="Asteroid Blaster" bodyClassName="standalone-game-preview__body"><AsteroidsGame gateway={new HttpAsteroidsGateway()} /></InGameShell></StrictMode>)
