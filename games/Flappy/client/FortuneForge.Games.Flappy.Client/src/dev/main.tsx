import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import '../../../../../../fortuneforge.client/src/dev/standaloneGamePreview.css'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { FlappyGame } from '../FlappyGame'
import { HttpFlappyGateway } from '../httpFlappyGateway'
import '../flappy.css'

const root = document.getElementById('root')
if (!root) throw new Error('Flappy preview root was not found.')
createRoot(root).render(<StrictMode><InGameShell title="Flappy" bodyClassName="standalone-game-preview__body"><FlappyGame gateway={new HttpFlappyGateway()} /></InGameShell></StrictMode>)
