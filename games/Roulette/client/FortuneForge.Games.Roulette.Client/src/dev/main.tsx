import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import '../../../../../../fortuneforge.client/src/dev/standaloneGamePreview.css'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { HttpRouletteGateway } from '../httpRouletteGateway'
import { RouletteGame } from '../RouletteGame'
import '../roulette.css'

const root = document.getElementById('root')
if (!root) throw new Error('Roulette preview root was not found.')
createRoot(root).render(<StrictMode><InGameShell title="Roulette" bodyClassName="standalone-game-preview__body"><RouletteGame gateway={new HttpRouletteGateway()} /></InGameShell></StrictMode>)
