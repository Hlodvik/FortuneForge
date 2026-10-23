import {createRoot} from 'react-dom/client'
import {InGameShell} from '../../../../../../fortuneforge.client/src/components/InGameShell'
import {MinesGame} from '../MinesGame'
import './preview.css'

createRoot(document.getElementById('root')!).render(<InGameShell title="Mines" theme="casino" actions={<span className="mines-preview-mode">PRACTICE · PLAY CREDITS</span>}><MinesGame playerId="standalone-practice"/></InGameShell>)
