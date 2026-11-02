import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import '../../../../../../fortuneforge.client/src/dev/standaloneGamePreview.css'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { SnakeGame } from '../SnakeGame'
import { HttpSnakeGateway } from '../httpSnakeGateway'
import '../snake.css'

const root = document.getElementById('root')
if (!root) throw new Error('Snake preview root was not found.')
createRoot(root).render(<StrictMode><InGameShell title="Snake" bodyClassName="standalone-game-preview__body"><SnakeGame gateway={new HttpSnakeGateway()} /></InGameShell></StrictMode>)
