import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { AsteroidsGame } from '../AsteroidsGame'
import { HttpAsteroidsGateway } from '../httpAsteroidsGateway'
import '../asteroids.css'
import '../asteroidsLeaderboard.css'
import '../asteroidsViewport.css'

const root = document.getElementById('root')
if (!root) throw new Error('Asteroids preview root was not found.')
createRoot(root).render(<StrictMode><AsteroidsGame gateway={new HttpAsteroidsGateway()} /></StrictMode>)
