import { createRoot } from 'react-dom/client'
import { DragonTigerGame } from '../DragonTigerGame'

createRoot(document.getElementById('root')!).render(<main style={{ maxWidth: 1320, margin: 'auto', padding: 'clamp(8px, 2vw, 24px)' }}><DragonTigerGame playerId="standalone-practice"/></main>)
