import { InGameShell } from '../../../../../../fortuneforge.client/src/components/InGameShell'
import '../../../../../../fortuneforge.client/src/dev/standaloneGamePreview.css'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { TwentyFortyEightGame } from '../TwentyFortyEightGame'
import { HttpTwentyFortyEightGateway } from '../httpTwentyFortyEightGateway'
import '../twentyFortyEight.css'
import '../twentyFortyEightAccessibility.css'
import '../twentyFortyEightViewport.css'

const root = document.getElementById('root')
if (!root) throw new Error('2048 preview root was not found.')
createRoot(root).render(<StrictMode><InGameShell title="2048" bodyClassName="standalone-game-preview__body"><TwentyFortyEightGame embedded gateway={new HttpTwentyFortyEightGateway()} /></InGameShell></StrictMode>)
