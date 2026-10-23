import { createRoot } from 'react-dom/client'
import { DragonTigerGame } from '../DragonTigerGame'
import './preview.css'

createRoot(document.getElementById('root')!).render(<main><DragonTigerGame playerId="standalone-practice"/></main>)
