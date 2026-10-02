import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { HttpRouletteGateway } from '../httpRouletteGateway'
import { RouletteGame } from '../RouletteGame'
import '../roulette.css'

const root = document.getElementById('root')
if (!root) throw new Error('Roulette preview root was not found.')
createRoot(root).render(<StrictMode><RouletteGame gateway={new HttpRouletteGateway()} /></StrictMode>)
