import { createRoot } from 'react-dom/client'
import { HiLoGame } from '../HiLoGame'
import { InGameNavExits } from '../../../../../../fortuneforge.client/src/components/InGameNavbar'
import './preview.css'

createRoot(document.getElementById('root')!).render(<div className="hilo-preview"><header className="hilo-preview-nav"><InGameNavExits /><span>Practice play</span></header><main><HiLoGame playerId="standalone-practice" /></main></div>)
