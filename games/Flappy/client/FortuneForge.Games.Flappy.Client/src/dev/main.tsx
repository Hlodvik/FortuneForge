import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { FlappyGame } from '../FlappyGame'
import { HttpFlappyGateway } from '../httpFlappyGateway'
import '../flappy.css'

const root = document.getElementById('root')
if (!root) throw new Error('Flappy preview root was not found.')
createRoot(root).render(<StrictMode><FlappyGame gateway={new HttpFlappyGateway()} /></StrictMode>)
