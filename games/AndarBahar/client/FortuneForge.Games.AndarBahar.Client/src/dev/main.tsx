import { createRoot } from 'react-dom/client'
import { AndarBaharGame } from '../AndarBaharGame'
import './preview.css'
createRoot(document.getElementById('root')!).render(<main className="ab-preview"><nav aria-label="Preview context"><span>FORTUNEFORGE</span><span>DEVELOPMENT PRACTICE <b>·</b> NO CASH VALUE</span></nav><AndarBaharGame playerId="standalone-practice"/></main>)
